using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>Škálování krok 3: počet řádků jedné tabulky před a po převodu.</summary>
public sealed record TableCopyResult(string Table, long SourceRows, long TargetRows)
{
    public bool Matches => SourceRows == TargetRows;
}

/// <summary>
/// Škálování krok 3: převod dat jednoho modulu ze SQLite do PostgreSQL (příkaz <c>migrate-to-postgres</c>).
/// </summary>
/// <remarks>
/// <para>Řídí se relačním modelem EF (tabulky, sloupce, cizí klíče) – nový sloupec nebo tabulka se převede bez úpravy kódu.
/// Hodnoty převádí podle typu cílového sloupce: SQLite ukládá GUID, čas i desetinné číslo jako text, bool jako 0/1.</para>
/// <para>Celý modul v jedné transakci: smazat cílové tabulky → zkopírovat řádky se stejnými ID (v pořadí podle cizích klíčů)
/// → posunout čítače identity → porovnat počty řádků. Při jakékoli chybě nebo neshodě se nic neuloží.</para>
/// <para>Pojistky proti přepsání nových dat:
/// 1) značka <see cref="MarkerTable"/> ve schématu modulu – modul už jednou převedený se znovu převede jen s
/// <paramref name="replace"/> (<c>--replace</c>, např. po návratu na SQLite); zachytí i pouhé úpravy řádků (změna hesla);
/// 2) cílová tabulka smí obsahovat jen řádky, které jsou i v SQLite (podle primárního klíče) – typicky data z migrací
/// („Nezařazeno“, role). Nové řádky zapsané aplikací převod odmítne vždy, i s --replace.</para>
/// </remarks>
public static class SqliteToPostgresCopier
{
    /// <summary>Tabulka ve schématu modulu: kdy proběhl převod. Mimo model EF (jako historie migrací).</summary>
    public const string MarkerTable = "__MiniShopDataMigration";

    public static async Task<IReadOnlyList<TableCopyResult>> CopyAsync(
        DbContext source, DbContext target, bool replace = false, CancellationToken cancellationToken = default)
    {
        if (!source.Database.IsSqlite() || !target.Database.IsNpgsql())
        {
            throw new InvalidOperationException("Převod jde jen ze SQLite do PostgreSQL.");
        }

        // Obě databáze na nejnovější schéma (stejně jako start aplikace).
        await source.Database.MigrateAsync(cancellationToken);
        await target.Database.MigrateAsync(cancellationToken);

        var sourceTables = source.Model.GetRelationalModel().Tables.ToDictionary(t => t.Name);
        var targetTables = OrderByForeignKeys(target.Model.GetRelationalModel().Tables.ToList());

        var sourceConnection = source.Database.GetDbConnection();
        await source.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await target.Database.BeginTransactionAsync(cancellationToken);
            var targetConnection = (NpgsqlConnection)target.Database.GetDbConnection();
            var schema = targetTables.Select(t => t.Schema).Distinct().Single()
                ?? throw new InvalidOperationException("Cílové tabulky nemají schéma.");
            var marker = $"{Quote(schema)}.{Quote(MarkerTable)}";

            await ExecuteAsync(targetConnection,
                $"CREATE TABLE IF NOT EXISTS {marker} (\"MigratedAtUtc\" timestamp with time zone NOT NULL)", cancellationToken);
            await using (var previous = new NpgsqlCommand($"SELECT MAX(\"MigratedAtUtc\") FROM {marker}", targetConnection))
            {
                if (await previous.ExecuteScalarAsync(cancellationToken) is DateTime migratedAt && !replace)
                {
                    throw new InvalidOperationException(
                        $"Modul už byl převeden ({migratedAt:u}). Znovu jen vědomě s --replace – změny v PostgreSQL od té doby by se ztratily.");
                }
            }

            foreach (var table in targetTables)
            {
                await EnsureOnlySourceRowsAsync(sourceConnection, sourceTables[table.Name], targetConnection, table, cancellationToken);
            }

            await ExecuteAsync(targetConnection,
                $"TRUNCATE {string.Join(", ", targetTables.Select(Qualified))} RESTART IDENTITY CASCADE", cancellationToken);

            var results = new List<TableCopyResult>();
            foreach (var table in targetTables)
            {
                var copied = await CopyTableAsync(sourceConnection, sourceTables[table.Name], targetConnection, table, cancellationToken);
                await ResetIdentityAsync(targetConnection, table, cancellationToken);
                var targetRows = await CountAsync(targetConnection, Qualified(table), cancellationToken);
                results.Add(new TableCopyResult(table.Name, copied, targetRows));
            }

            if (results.Any(r => !r.Matches))
            {
                throw new InvalidOperationException("Počty řádků nesouhlasí: " +
                    string.Join(", ", results.Where(r => !r.Matches).Select(r => $"{r.Table} {r.SourceRows} × {r.TargetRows}")));
            }

            await ExecuteAsync(targetConnection, $"INSERT INTO {marker} VALUES (now())", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return results;
        }
        finally
        {
            await source.Database.CloseConnectionAsync();
        }
    }

    private static async Task<long> CopyTableAsync(DbConnection source, ITable sourceTable, NpgsqlConnection target, ITable table,
        CancellationToken cancellationToken)
    {
        var columns = table.Columns.ToList();
        await using var read = source.CreateCommand();
        read.CommandText = $"SELECT {string.Join(", ", columns.Select(c => Quote(SourceColumn(sourceTable, c))))} FROM {Quote(sourceTable.Name)}";

        await using var insert = new NpgsqlCommand(
            $"INSERT INTO {Qualified(table)} ({string.Join(", ", columns.Select(c => Quote(c.Name)))}) " +
            $"OVERRIDING SYSTEM VALUE VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})", target);

        long rows = 0;
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            insert.Parameters.Clear();
            for (var i = 0; i < columns.Count; i++)
            {
                insert.Parameters.Add(new NpgsqlParameter($"p{i}", Convert(reader.GetValue(i), columns[i])));
            }

            await insert.ExecuteNonQueryAsync(cancellationToken);
            rows++;
        }

        return rows;
    }

    /// <summary>Hodnota ze SQLite (long, double, string, byte[]) → typ, který čeká sloupec v PostgreSQL.</summary>
    public static object Convert(object value, IColumn column)
    {
        if (value is DBNull)
        {
            return DBNull.Value;
        }

        var type = Nullable.GetUnderlyingType(column.ProviderClrType) ?? column.ProviderClrType;
        var invariant = CultureInfo.InvariantCulture;
        return type switch
        {
            _ when type == typeof(string) => System.Convert.ToString(value, invariant)!,
            _ when type == typeof(Guid) => value is byte[] bytes ? new Guid(bytes) : Guid.Parse((string)value),
            _ when type == typeof(bool) => System.Convert.ToInt64(value, invariant) != 0,
            _ when type == typeof(decimal) => value is string text
                ? decimal.Parse(text, NumberStyles.Float, invariant)
                : System.Convert.ToDecimal(value, invariant),
            _ when type == typeof(DateTime) => value is string text
                ? DateTime.Parse(text, invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                : throw new InvalidOperationException($"Nečekaný čas v {column.Name}: {value}"),
            _ when type == typeof(DateTimeOffset) => DateTimeOffset.Parse((string)value, invariant, DateTimeStyles.AssumeUniversal)
                .ToUniversalTime(),
            _ when type == typeof(TimeSpan) => TimeSpan.Parse((string)value, invariant),
            _ when type == typeof(byte[]) => value,
            _ when type.IsEnum => Enum.ToObject(type, System.Convert.ToInt64(value, invariant)),
            _ => System.Convert.ChangeType(value, type, invariant)
        };
    }

    /// <summary>Pojistka: v cílové tabulce smí být jen řádky, které jsou i v SQLite (data z migrací), jinak by převod přepsal nová data.</summary>
    private static async Task EnsureOnlySourceRowsAsync(DbConnection source, ITable sourceTable, NpgsqlConnection target, ITable table,
        CancellationToken cancellationToken)
    {
        if (await CountAsync(target, Qualified(table), cancellationToken) == 0)
        {
            return;
        }

        var key = table.PrimaryKey?.Columns.ToList()
            ?? throw new InvalidOperationException($"Tabulka {table.Name} nemá primární klíč.");

        var sourceKeys = new HashSet<string>();
        await using (var read = source.CreateCommand())
        {
            read.CommandText = $"SELECT {string.Join(", ", key.Select(c => Quote(SourceColumn(sourceTable, c))))} FROM {Quote(sourceTable.Name)}";
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sourceKeys.Add(KeyText(key.Select((c, i) => Convert(reader.GetValue(i), c))));
            }
        }

        await using var command = new NpgsqlCommand(
            $"SELECT {string.Join(", ", key.Select(c => Quote(c.Name)))} FROM {Qualified(table)}", target);
        await using var targetReader = await command.ExecuteReaderAsync(cancellationToken);
        while (await targetReader.ReadAsync(cancellationToken))
        {
            var targetKey = KeyText(key.Select((_, i) => targetReader.GetValue(i)));
            if (!sourceKeys.Contains(targetKey))
            {
                throw new InvalidOperationException(
                    $"Tabulka {Qualified(table)} už obsahuje data, která v SQLite nejsou (klíč {targetKey}). " +
                    "Aplikace zřejmě už běží nad PostgreSQL – převod by nová data přepsal, proto se nespustí.");
            }
        }
    }

    private static string KeyText(IEnumerable<object> values) =>
        string.Join("|", values.Select(v => System.Convert.ToString(v, CultureInfo.InvariantCulture)?.ToLowerInvariant()));

    /// <summary>Po vložení ID ručně je potřeba posunout čítač – další objednávka jinak dostane číslo, které už existuje.</summary>
    private static async Task ResetIdentityAsync(NpgsqlConnection target, ITable table, CancellationToken cancellationToken)
    {
        foreach (var column in table.Columns.Where(c => c.ProviderClrType == typeof(int) || c.ProviderClrType == typeof(long)))
        {
            await using var command = new NpgsqlCommand("SELECT pg_get_serial_sequence(@table, @column)", target);
            command.Parameters.AddWithValue("table", Qualified(table));
            command.Parameters.AddWithValue("column", column.Name);
            if (await command.ExecuteScalarAsync(cancellationToken) is string sequence)
            {
                await ExecuteAsync(target,
                    $"SELECT setval('{sequence}', COALESCE((SELECT MAX({Quote(column.Name)}) FROM {Qualified(table)}), 0) + 1, false)",
                    cancellationToken);
            }
        }
    }

    /// <summary>Nejdřív tabulky, na které ostatní odkazují (Orders před OrderItems) – cizí klíče platí hned při vložení.</summary>
    private static List<ITable> OrderByForeignKeys(List<ITable> tables)
    {
        var ordered = new List<ITable>();
        var visiting = new HashSet<ITable>();

        void Visit(ITable table)
        {
            if (ordered.Contains(table) || !visiting.Add(table))
            {
                return;
            }

            foreach (var principal in table.ForeignKeyConstraints.Select(fk => fk.PrincipalTable).Where(t => t != table))
            {
                Visit(principal);
            }

            ordered.Add(table);
        }

        tables.OrderBy(t => t.Name, StringComparer.Ordinal).ToList().ForEach(Visit);
        return ordered;
    }

    private static string SourceColumn(ITable sourceTable, IColumn targetColumn) =>
        sourceTable.FindColumn(targetColumn.Name)?.Name
        ?? throw new InvalidOperationException($"Sloupec {sourceTable.Name}.{targetColumn.Name} v SQLite chybí – je SQLite po migracích?");

    private static async Task<long> CountAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return System.Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Qualified(ITable table) => table.Schema is null ? Quote(table.Name) : $"{Quote(table.Schema)}.{Quote(table.Name)}";

    private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
}

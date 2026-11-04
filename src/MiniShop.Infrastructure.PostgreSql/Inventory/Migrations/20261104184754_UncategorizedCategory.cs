using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.PostgreSql.Inventory.Migrations
{
    /// <summary>
    /// Škálování krok 2 (našly testy nad PostgreSQL): kategorie „Nezařazeno“ jako v SQLite. Tam ji zakládá ručně dopsaná
    /// migrace CategoriesAndAvailability (e-shop krok 2); InitialPostgres vznikla z modelu a data v ní nebyla.
    /// Jen PostgreSQL – SQLite ji má. Vloží se jen tehdy, když chybí, ID přidělí databáze (v nové databázi 1);
    /// databáze, které už kategorie mají (lokální AppHost ze škálování kroku 1), se nerozbijí.
    /// </summary>
    public partial class UncategorizedCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO inventory."Categories" ("Name")
                SELECT 'Nezařazeno'
                WHERE NOT EXISTS (SELECT 1 FROM inventory."Categories" WHERE "Name" = 'Nezařazeno');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Kategorie mohla mezitím dostat produkty – zpět se nemaže.
        }
    }
}

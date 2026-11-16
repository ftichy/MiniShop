using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Accounts.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Účty krok 12A: sedm zaměstnaneckých rolí místo jedné role Admin. Data (ruční SQL – EF ho nevygeneruje):
    /// založí role (když chybí) a každému, kdo má roli Admin, přidá všechny zaměstnanecké role – nikdo nepřijde o práva.
    /// Role Admin u účtů zůstává (expand → contract): starší verze kódu ji při postupném nasazení ještě čte.
    /// Sloupec <c>Detail</c> v auditu: co přesně se na rolích změnilo.
    /// </summary>
    public partial class StaffRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Detail",
                schema: "accounts",
                table: "AccountAuditEntries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO accounts."AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                SELECT gen_random_uuid()::text, r.name, upper(r.name), gen_random_uuid()::text
                FROM (VALUES ('UserAdmin'), ('CatalogManager'), ('Warehouse'), ('CustomerSupport'),
                             ('Marketing'), ('Accounting'), ('Operations')) AS r(name)
                WHERE NOT EXISTS (SELECT 1 FROM accounts."AspNetRoles" x WHERE x."NormalizedName" = upper(r.name));

                INSERT INTO accounts."AspNetUserRoles" ("UserId", "RoleId")
                SELECT userRole."UserId", staff."Id"
                FROM accounts."AspNetUserRoles" userRole
                JOIN accounts."AspNetRoles" admin ON admin."Id" = userRole."RoleId" AND admin."NormalizedName" = 'ADMIN'
                CROSS JOIN accounts."AspNetRoles" staff
                WHERE staff."NormalizedName" IN ('USERADMIN', 'CATALOGMANAGER', 'WAREHOUSE', 'CUSTOMERSUPPORT',
                                                 'MARKETING', 'ACCOUNTING', 'OPERATIONS')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Detail",
                schema: "accounts",
                table: "AccountAuditEntries");
        }
    }
}

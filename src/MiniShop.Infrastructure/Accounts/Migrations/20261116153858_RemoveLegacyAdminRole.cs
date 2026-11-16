using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Accounts.Migrations
{
    /// <summary>
    /// Účty krok 12B (contract ke kroku 12A): smaže původní roli Admin. Nasazená verze 12A ji už nečte, takže postupnému
    /// nasazení nevadí. Pojistka: kdo by ji pořád měl bez zaměstnaneckých rolí (účet vzniklý mezi migrací a nasazením 12A),
    /// dostane je nejdřív – nikdo nepřijde o práva.
    /// </summary>
    public partial class RemoveLegacyAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO accounts."AspNetUserRoles" ("UserId", "RoleId")
                SELECT userRole."UserId", staff."Id"
                FROM accounts."AspNetUserRoles" userRole
                JOIN accounts."AspNetRoles" admin ON admin."Id" = userRole."RoleId" AND admin."NormalizedName" = 'ADMIN'
                CROSS JOIN accounts."AspNetRoles" staff
                WHERE staff."NormalizedName" IN ('USERADMIN', 'CATALOGMANAGER', 'WAREHOUSE', 'CUSTOMERSUPPORT',
                                                 'MARKETING', 'ACCOUNTING', 'OPERATIONS')
                ON CONFLICT DO NOTHING;

                DELETE FROM accounts."AspNetUserRoles"
                WHERE "RoleId" IN (SELECT "Id" FROM accounts."AspNetRoles" WHERE "NormalizedName" = 'ADMIN');

                DELETE FROM accounts."AspNetRoles" WHERE "NormalizedName" = 'ADMIN';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Zpět nic – role Admin se nevrací (práva drží zaměstnanecké role).
        }
    }
}

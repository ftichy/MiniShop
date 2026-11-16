using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Invoicing.Migrations
{
    /// <summary>
    /// E-shop krok 22: víc dobropisů k jedné faktuře (vratky po částech). Sloupec <c>ReturnNumber</c> (0 = celý dobropis –
    /// i všechny stávající) a unikátní index objednávka + číslo vratky místo unikátní objednávky. Starší verze kódu zapisuje
    /// vždy 0, takže ji nový index chrání stejně jako ten starý (postupné nasazení).
    /// </summary>
    public partial class PartialReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnNumber",
                schema: "invoicing",
                table: "CreditNotes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrderId_ReturnNumber",
                schema: "invoicing",
                table: "CreditNotes",
                columns: new[] { "OrderId", "ReturnNumber" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_OrderId",
                schema: "invoicing",
                table: "CreditNotes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_OrderId_ReturnNumber",
                schema: "invoicing",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "ReturnNumber",
                schema: "invoicing",
                table: "CreditNotes");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrderId",
                schema: "invoicing",
                table: "CreditNotes",
                column: "OrderId",
                unique: true);
        }
    }
}

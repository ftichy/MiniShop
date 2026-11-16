using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Inventory.Migrations
{
    /// <summary>
    /// E-shop krok 22: vratky po částech – vrácené množství na rezervaci (výchozí 0, starší verze kódu ho nezná)
    /// a tabulka naskladněných vratek. Jen přidává (expand).
    /// </summary>
    public partial class PartialReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnedQuantity",
                schema: "inventory",
                table: "StockReservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Ruční SQL (EF data nepřevádí): rezervace vrácené celé před krokem 22 mají vrácené všechny kusy.
            migrationBuilder.Sql("""
                UPDATE inventory."StockReservations" SET "ReturnedQuantity" = "Quantity" WHERE "ReturnedAtUtc" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "StockReturns",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    ReturnNumber = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    RestockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReturns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReturns_OrderId_ReturnNumber",
                schema: "inventory",
                table: "StockReturns",
                columns: new[] { "OrderId", "ReturnNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockReturns",
                schema: "inventory");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                schema: "inventory",
                table: "StockReservations");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <summary>
    /// E-shop krok 22: vratky po částech v Orderingu – tabulky <c>OrderReturns</c> + <c>OrderReturnLines</c> a klíč ságy vratky
    /// objednávka + <c>ReturnNumber</c> (výchozí 0 = vratka celé objednávky, i všechny stávající ságy). Starší verze kódu zapisuje
    /// ságy bez čísla (= 0) a hledá je podle objednávky – při postupném nasazení funguje dál.
    /// </summary>
    public partial class PartialReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ReturnSagas",
                schema: "ordering",
                table: "ReturnSagas");

            migrationBuilder.AddColumn<int>(
                name: "ReturnNumber",
                schema: "ordering",
                table: "ReturnSagas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReturnSagas",
                schema: "ordering",
                table: "ReturnSagas",
                columns: new[] { "OrderId", "ReturnNumber" });

            migrationBuilder.CreateTable(
                name: "OrderReturns",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderReturns_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderReturnLines",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderReturnId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderReturnLines_OrderReturns_OrderReturnId",
                        column: x => x.OrderReturnId,
                        principalSchema: "ordering",
                        principalTable: "OrderReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturnLines_OrderReturnId",
                schema: "ordering",
                table: "OrderReturnLines",
                column: "OrderReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReturns_OrderId_Number",
                schema: "ordering",
                table: "OrderReturns",
                columns: new[] { "OrderId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderReturnLines",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "OrderReturns",
                schema: "ordering");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReturnSagas",
                schema: "ordering",
                table: "ReturnSagas");

            migrationBuilder.DropColumn(
                name: "ReturnNumber",
                schema: "ordering",
                table: "ReturnSagas");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReturnSagas",
                schema: "ordering",
                table: "ReturnSagas",
                column: "OrderId");
        }
    }
}

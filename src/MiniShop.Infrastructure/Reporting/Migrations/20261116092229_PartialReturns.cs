using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Reporting.Migrations
{
    /// <summary>
    /// E-shop krok 22: vratky po částech v přehledu tržeb – nové tabulky <c>SalesOrderReturns</c> (částka, unikátní objednávka
    /// + číslo vratky) a <c>SalesOrderReturnLines</c> (vrácené kusy). Jen přidává – starší verze kódu je nezná a nevadí jí.
    /// </summary>
    public partial class PartialReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesOrderReturnLines",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnNumber = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderReturnLines_SalesOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "reporting",
                        principalTable: "SalesOrders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderReturns",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReturnNumber = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderReturns_SalesOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "reporting",
                        principalTable: "SalesOrders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderReturnLines_OrderId",
                schema: "reporting",
                table: "SalesOrderReturnLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderReturns_OrderId_ReturnNumber",
                schema: "reporting",
                table: "SalesOrderReturns",
                columns: new[] { "OrderId", "ReturnNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderReturnLines",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "SalesOrderReturns",
                schema: "reporting");
        }
    }
}

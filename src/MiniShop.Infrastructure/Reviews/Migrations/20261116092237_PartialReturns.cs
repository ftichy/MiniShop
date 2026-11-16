using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Reviews.Migrations
{
    /// <summary>
    /// E-shop krok 22: produkty vrácené celé vratkami po částech (<c>PurchasedReturnedProducts</c>) – ty z objednávky hodnotit
    /// nejde. Jen přidává – starší verze kódu tabulku nezná a nevadí jí.
    /// </summary>
    public partial class PartialReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchasedReturnedProducts",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasedReturnedProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasedReturnedProducts_PurchasedOrders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "reviews",
                        principalTable: "PurchasedOrders",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasedReturnedProducts_OrderId_ProductId",
                schema: "reviews",
                table: "PurchasedReturnedProducts",
                columns: new[] { "OrderId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchasedReturnedProducts",
                schema: "reviews");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class ProductStockConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "inventory",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WarehouseStocks_Quantity_NonNegative",
                schema: "inventory",
                table: "WarehouseStocks",
                sql: "\"Quantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_StockQuantity_NonNegative",
                schema: "inventory",
                table: "Products",
                sql: "\"StockQuantity\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WarehouseStocks_Quantity_NonNegative",
                schema: "inventory",
                table: "WarehouseStocks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_StockQuantity_NonNegative",
                schema: "inventory",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "inventory",
                table: "Products");
        }
    }
}

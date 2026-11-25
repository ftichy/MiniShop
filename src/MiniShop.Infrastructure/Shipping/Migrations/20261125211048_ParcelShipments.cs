using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Shipping.Migrations
{
    /// <inheritdoc />
    public partial class ParcelShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shipments_OrderId",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.AddColumn<int>(
                name: "ParcelNumber",
                schema: "shipping",
                table: "Shipments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId_ParcelNumber",
                schema: "shipping",
                table: "Shipments",
                columns: new[] { "OrderId", "ParcelNumber" },
                unique: true,
                filter: "\"Status\" = 'Created'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shipments_OrderId_ParcelNumber",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ParcelNumber",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId",
                schema: "shipping",
                table: "Shipments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" = 'Created'");
        }
    }
}

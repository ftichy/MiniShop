using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class CarrierShipmentRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShipmentError",
                schema: "ordering",
                table: "Orders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShipmentRequestedAtUtc",
                schema: "ordering",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipmentError",
                schema: "ordering",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipmentRequestedAtUtc",
                schema: "ordering",
                table: "Orders");
        }
    }
}

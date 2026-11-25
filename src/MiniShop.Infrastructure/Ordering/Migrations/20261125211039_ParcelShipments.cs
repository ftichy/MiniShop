using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class ParcelShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Carrier",
                schema: "ordering",
                table: "OrderParcels",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAtUtc",
                schema: "ordering",
                table: "OrderParcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedToSenderAtUtc",
                schema: "ordering",
                table: "OrderParcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipmentError",
                schema: "ordering",
                table: "OrderParcels",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShipmentRequestedAtUtc",
                schema: "ordering",
                table: "OrderParcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippedAtUtc",
                schema: "ordering",
                table: "OrderParcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                schema: "ordering",
                table: "OrderParcels",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Carrier",
                schema: "ordering",
                table: "OrderParcels");

            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                schema: "ordering",
                table: "OrderParcels");

            migrationBuilder.DropColumn(
                name: "ReturnedToSenderAtUtc",
                schema: "ordering",
                table: "OrderParcels");

            migrationBuilder.DropColumn(
                name: "ShipmentError",
                schema: "ordering",
                table: "OrderParcels");

            migrationBuilder.DropColumn(
                name: "ShipmentRequestedAtUtc",
                schema: "ordering",
                table: "OrderParcels");

            migrationBuilder.DropColumn(
                name: "ShippedAtUtc",
                schema: "ordering",
                table: "OrderParcels");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                schema: "ordering",
                table: "OrderParcels");
        }
    }
}

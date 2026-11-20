using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Shipping.Migrations
{
    /// <inheritdoc />
    public partial class CarrierTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCarrierUpdateAtUtc",
                schema: "shipping",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCheckedAtUtc",
                schema: "shipping",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastSequence",
                schema: "shipping",
                table: "Shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UnknownAtCarrier",
                schema: "shipping",
                table: "Shipments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "shipping",
                table: "Shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ShipmentTrackingEvents",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentTrackingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentTrackingEvents_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "shipping",
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TrackingNumber",
                schema: "shipping",
                table: "Shipments",
                column: "TrackingNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentTrackingEvents_ShipmentId_Sequence",
                schema: "shipping",
                table: "ShipmentTrackingEvents",
                columns: new[] { "ShipmentId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentTrackingEvents",
                schema: "shipping");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_TrackingNumber",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "LastCarrierUpdateAtUtc",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "LastCheckedAtUtc",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "LastSequence",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "UnknownAtCarrier",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "shipping",
                table: "Shipments");
        }
    }
}

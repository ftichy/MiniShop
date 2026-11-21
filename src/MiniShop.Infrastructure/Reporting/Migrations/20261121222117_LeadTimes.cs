using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class LeadTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderLeadTimes",
                schema: "reporting",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    PlacedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShippedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ByCarrier = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLeadTimes", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "StreamCheckpoints",
                schema: "reporting",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Offset = table.Column<long>(type: "bigint", nullable: true),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamCheckpoints", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLeadTimes_ConfirmedAtUtc",
                schema: "reporting",
                table: "OrderLeadTimes",
                column: "ConfirmedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLeadTimes_DeliveredAtUtc",
                schema: "reporting",
                table: "OrderLeadTimes",
                column: "DeliveredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLeadTimes_ShippedAtUtc",
                schema: "reporting",
                table: "OrderLeadTimes",
                column: "ShippedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderLeadTimes",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "StreamCheckpoints",
                schema: "reporting");
        }
    }
}

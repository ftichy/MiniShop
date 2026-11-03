using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class Returns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "Orders",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnRejectionReason",
                table: "Orders",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReturnSagas",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GoodsDeadlineUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequiresRefund = table.Column<bool>(type: "INTEGER", nullable: false),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnSagas", x => x.OrderId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnSagas_State_GoodsDeadlineUtc",
                table: "ReturnSagas",
                columns: new[] { "State", "GoodsDeadlineUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnSagas");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReturnRejectionReason",
                table: "Orders");
        }
    }
}

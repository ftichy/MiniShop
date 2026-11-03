using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDeadlineUtc",
                table: "OrderSagas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RefundRequested",
                table: "OrderSagas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresPayment",
                table: "OrderSagas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Orders",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");   // ručně: dosavadní objednávky = bez platby (jako objednávky z API)

            migrationBuilder.CreateIndex(
                name: "IX_OrderSagas_State_PaymentDeadlineUtc",
                table: "OrderSagas",
                columns: new[] { "State", "PaymentDeadlineUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderSagas_State_PaymentDeadlineUtc",
                table: "OrderSagas");

            migrationBuilder.DropColumn(
                name: "PaymentDeadlineUtc",
                table: "OrderSagas");

            migrationBuilder.DropColumn(
                name: "RefundRequested",
                table: "OrderSagas");

            migrationBuilder.DropColumn(
                name: "RequiresPayment",
                table: "OrderSagas");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Orders");
        }
    }
}

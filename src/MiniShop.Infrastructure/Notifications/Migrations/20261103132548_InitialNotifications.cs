using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderRecipients",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CustomerEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ShippingMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ShippingPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    TotalPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRecipients", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TraceParent = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Handler = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.EventId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "OrderRecipientItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRecipientItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRecipientItems_OrderRecipients_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderRecipients",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRecipientItems_OrderId",
                table: "OrderRecipientItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRecipients_CustomerId",
                table: "OrderRecipients",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAtUtc",
                table: "OutboxMessages",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRecipientItems");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "ProcessedMessages");

            migrationBuilder.DropTable(
                name: "OrderRecipients");
        }
    }
}

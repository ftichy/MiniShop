using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.PostgreSql.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "OrderRecipients",
                schema: "notifications",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShippingMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ShippingPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRecipients", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TraceParent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                schema: "notifications",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handler = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.EventId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "OrderRecipientItems",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRecipientItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRecipientItems_OrderRecipients_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "notifications",
                        principalTable: "OrderRecipients",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRecipientItems_OrderId",
                schema: "notifications",
                table: "OrderRecipientItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRecipients_CustomerId",
                schema: "notifications",
                table: "OrderRecipients",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAtUtc",
                schema: "notifications",
                table: "OutboxMessages",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMessages_ProcessedAtUtc",
                schema: "notifications",
                table: "ProcessedMessages",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRecipientItems",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "ProcessedMessages",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "OrderRecipients",
                schema: "notifications");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class OrderStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderStatusChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStatusChanges_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusChanges_OrderId",
                table: "OrderStatusChanges",
                column: "OrderId");

            // E-shop krok 5: historie pro existující objednávky. Vznik (Pending) má známý čas – CreatedAtUtc.
            // Pozdější stav (Confirmed, Completed…) doplníme bez času: kdy nastal, se už nezjistí a vymýšlet ho nebudeme.
            // Pořadí příkazů = pořadí Id → v historii je Pending vždy první.
            migrationBuilder.Sql(
                """
                INSERT INTO "OrderStatusChanges" ("OrderId", "Status", "ChangedAtUtc", "Note")
                SELECT "Id", 'Pending', "CreatedAtUtc", NULL FROM "Orders" ORDER BY "Id";
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO "OrderStatusChanges" ("OrderId", "Status", "ChangedAtUtc", "Note")
                SELECT "Id", "Status", NULL, "RejectionReason" FROM "Orders" WHERE "Status" <> 'Pending' ORDER BY "Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderStatusChanges");
        }
    }
}

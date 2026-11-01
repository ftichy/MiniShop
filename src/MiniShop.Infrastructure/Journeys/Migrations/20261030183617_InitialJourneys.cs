using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Journeys.Migrations
{
    /// <inheritdoc />
    public partial class InitialJourneys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JourneyEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JourneyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ValueText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_EventId",
                table: "JourneyEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_JourneyId_OffsetMs",
                table: "JourneyEvents",
                columns: new[] { "JourneyId", "OffsetMs" });

            migrationBuilder.CreateIndex(
                name: "IX_JourneyEvents_OrderId",
                table: "JourneyEvents",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JourneyEvents");
        }
    }
}

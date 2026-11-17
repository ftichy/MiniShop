using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class StaffAlertSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffAlertSubscriptions",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffAlertSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffAlertSubscriptions_Kind",
                schema: "notifications",
                table: "StaffAlertSubscriptions",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_StaffAlertSubscriptions_UserId_Kind",
                schema: "notifications",
                table: "StaffAlertSubscriptions",
                columns: new[] { "UserId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffAlertSubscriptions",
                schema: "notifications");
        }
    }
}

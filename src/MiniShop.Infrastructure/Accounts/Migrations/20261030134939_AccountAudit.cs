using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AccountAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TargetUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountAuditEntries_TargetUserId",
                table: "AccountAuditEntries",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountAuditEntries");
        }
    }
}

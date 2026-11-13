using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class LoginAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoginAttempts",
                schema: "accounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_OccurredAtUtc",
                schema: "accounts",
                table: "LoginAttempts",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_UserId",
                schema: "accounts",
                table: "LoginAttempts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginAttempts",
                schema: "accounts");
        }
    }
}

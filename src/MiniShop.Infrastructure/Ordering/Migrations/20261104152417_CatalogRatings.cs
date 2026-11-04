using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class CatalogRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RatingAverage",
                table: "CatalogProducts",
                type: "TEXT",
                precision: 3,
                scale: 1,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "RatingChangedAtUtc",
                table: "CatalogProducts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "CatalogProducts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatingAverage",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "RatingChangedAtUtc",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "CatalogProducts");
        }
    }
}

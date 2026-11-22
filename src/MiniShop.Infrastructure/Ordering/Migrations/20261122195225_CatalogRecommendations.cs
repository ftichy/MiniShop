using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class CatalogRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecommendationsComputedAtUtc",
                schema: "ordering",
                table: "CatalogProducts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<int>>(
                name: "RecommendedProductIds",
                schema: "ordering",
                table: "CatalogProducts",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecommendationsComputedAtUtc",
                schema: "ordering",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "RecommendedProductIds",
                schema: "ordering",
                table: "CatalogProducts");
        }
    }
}

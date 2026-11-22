using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Reporting.Migrations
{
    /// <inheritdoc />
    public partial class ProductRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductRecommendations",
                schema: "reporting",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecommendedProductIds = table.Column<List<int>>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRecommendations", x => x.ProductId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductRecommendations",
                schema: "reporting");
        }
    }
}

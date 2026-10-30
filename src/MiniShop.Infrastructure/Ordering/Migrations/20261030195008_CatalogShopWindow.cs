using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class CatalogShopWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // E-shop krok 2 – ručně upraveno: dosavadní produkty kopie katalogu dostupnost neznají („Unknown“, vygenerováno
            // bylo "" – to by při načtení neprošlo převodem na výčet). Po startu Webu je CatalogSyncOnStartupWorker
            // pozná jako neúplné a požádá sklad o celý katalog (SyncCatalogV1 → ProductUpdatedV2).
            migrationBuilder.AddColumn<string>(
                name: "Availability",
                table: "CatalogProducts",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<DateTime>(
                name: "AvailabilityChangedAtUtc",
                table: "CatalogProducts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "CatalogProducts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "CatalogProducts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "CatalogProducts",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageFileName",
                table: "CatalogProducts",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "CatalogProducts",
                type: "TEXT",
                maxLength: 800,
                nullable: false,
                defaultValue: "");

            // Do příchodu V2 aspoň název malými písmeny (SQLite lower() umí jen ASCII – diakritiku doplní V2 z C#).
            migrationBuilder.Sql("UPDATE \"CatalogProducts\" SET \"SearchText\" = lower(\"Name\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Availability",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "AvailabilityChangedAtUtc",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "ImageFileName",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "CatalogProducts");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <summary>
    /// E-shop krok 21: rozšíření pg_trgm (hledání podobných slov) + GIN index nad textem pro hledání. Jen přidává
    /// (expand) – starší verze kódu s ním běží dál.
    /// </summary>
    public partial class CatalogSearchTrigrams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProducts_SearchText",
                schema: "ordering",
                table: "CatalogProducts",
                column: "SearchText")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CatalogProducts_SearchText",
                schema: "ordering",
                table: "CatalogProducts");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}

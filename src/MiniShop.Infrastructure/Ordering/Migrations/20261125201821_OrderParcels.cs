using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class OrderParcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderParcels",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    WarehouseCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    WarehouseName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderParcels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderParcels_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderParcelLines",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    OrderParcelId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderParcelLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderParcelLines_OrderParcels_OrderParcelId",
                        column: x => x.OrderParcelId,
                        principalSchema: "ordering",
                        principalTable: "OrderParcels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderParcelLines_OrderParcelId",
                schema: "ordering",
                table: "OrderParcelLines",
                column: "OrderParcelId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderParcels_OrderId_Number",
                schema: "ordering",
                table: "OrderParcels",
                columns: new[] { "OrderId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderParcelLines",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "OrderParcels",
                schema: "ordering");
        }
    }
}

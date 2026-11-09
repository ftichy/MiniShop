using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniShop.Infrastructure.Invoicing.Migrations
{
    /// <inheritdoc />
    public partial class CreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreditReason",
                schema: "invoicing",
                table: "BillableOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundNote",
                schema: "invoicing",
                table: "BillableOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreditNoteNumberSeries",
                schema: "invoicing",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastSequence = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteNumberSeries", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                schema: "invoicing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ShippingMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShippingPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RefundNote = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditNoteLines",
                schema: "invoicing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditNoteId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNoteLines_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalSchema: "invoicing",
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_CreditNoteId",
                schema: "invoicing",
                table: "CreditNoteLines",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomerId",
                schema: "invoicing",
                table: "CreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_Number",
                schema: "invoicing",
                table: "CreditNotes",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrderId",
                schema: "invoicing",
                table: "CreditNotes",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditNoteLines",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "CreditNoteNumberSeries",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "CreditNotes",
                schema: "invoicing");

            migrationBuilder.DropColumn(
                name: "CreditReason",
                schema: "invoicing",
                table: "BillableOrders");

            migrationBuilder.DropColumn(
                name: "RefundNote",
                schema: "invoicing",
                table: "BillableOrders");
        }
    }
}

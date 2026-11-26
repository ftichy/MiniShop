using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniShop.Infrastructure.Invoicing.Migrations
{
    /// <inheritdoc />
    public partial class ReturnsBeforeInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "ReturnsBeforeInvoice",
                schema: "invoicing",
                table: "BillableOrders",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnsBeforeInvoice",
                schema: "invoicing",
                table: "BillableOrders");
        }
    }
}

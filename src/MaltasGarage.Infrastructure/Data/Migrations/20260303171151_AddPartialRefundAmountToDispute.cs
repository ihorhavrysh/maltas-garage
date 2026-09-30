using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaltasGarage.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartialRefundAmountToDispute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PartialRefundAmount",
                table: "Disputes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartialRefundAmount",
                table: "Disputes");
        }
    }
}

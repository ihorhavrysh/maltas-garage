using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaltasGarage.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsNewAndAuctionEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsNew",
                table: "Listings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AuctionEnabled",
                table: "Listings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "AuctionStartDate",
                table: "Listings",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsNew",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "AuctionEnabled",
                table: "Listings");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AuctionStartDate",
                table: "Listings",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}

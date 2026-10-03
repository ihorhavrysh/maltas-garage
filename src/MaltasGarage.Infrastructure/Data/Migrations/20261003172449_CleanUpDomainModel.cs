using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaltasGarage.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanUpDomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Orders_OrderId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_OrderId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ListingId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_OrderId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "DeliveryDeadline",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "EscrowReleaseDate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ListingId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "Conversations");

            migrationBuilder.AlterColumn<string>(
                name: "PreviousOrderStatus",
                table: "Disputes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            // The int column now holds "0".."7"; give it the enum names every other status column uses.
            // Delivered (3) and Cancelled (7) were never written and no longer exist
            migrationBuilder.Sql(@"
                UPDATE Disputes SET PreviousOrderStatus = CASE PreviousOrderStatus
                    WHEN '0' THEN 'Pending' WHEN '1' THEN 'Paid' WHEN '2' THEN 'Shipped'
                    WHEN '3' THEN 'Shipped' WHEN '4' THEN 'Completed' WHEN '5' THEN 'Disputed'
                    WHEN '6' THEN 'Refunded' WHEN '7' THEN 'Refunded' ELSE PreviousOrderStatus END;");

            // Rows that could still hold an enum value removed from the model (only demo data did)
            migrationBuilder.Sql("UPDATE Shipments SET Status = 'Shipped' WHERE Status IN ('InTransit', 'Delivered');");
            migrationBuilder.Sql("UPDATE Orders SET Status = 'Shipped' WHERE Status = 'Delivered';");
            migrationBuilder.Sql("UPDATE Orders SET Status = 'Refunded' WHERE Status = 'Cancelled';");
            migrationBuilder.Sql("UPDATE Listings SET Status = 'Active' WHERE Status = 'Draft';");
            migrationBuilder.Sql("UPDATE Payments SET Status = 'Pending' WHERE Status = 'Authorized';");

            // Keep the first review per order and author so the unique index can be created
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY OrderId, FromUserId ORDER BY CreatedAt, Id) AS n
                    FROM Reviews)
                DELETE FROM Reviews WHERE Id IN (SELECT Id FROM ranked WHERE n > 1);");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_OrderId_FromUserId",
                table: "Reviews",
                columns: new[] { "OrderId", "FromUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ListingId",
                table: "Orders",
                column: "ListingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Disputes SET PreviousOrderStatus = CASE PreviousOrderStatus
                    WHEN 'Pending' THEN '0' WHEN 'Paid' THEN '1' WHEN 'Shipped' THEN '2'
                    WHEN 'Completed' THEN '4' WHEN 'Disputed' THEN '5' WHEN 'Refunded' THEN '6'
                    ELSE '0' END;");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_OrderId_FromUserId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ListingId",
                table: "Orders");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "Shipments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryDeadline",
                table: "Shipments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscrowReleaseDate",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PreviousOrderStatus",
                table: "Disputes",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<Guid>(
                name: "ListingId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_OrderId",
                table: "Reviews",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ListingId",
                table: "Orders",
                column: "ListingId",
                unique: true,
                filter: "[ListingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OrderId",
                table: "Conversations",
                column: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Orders_OrderId",
                table: "Conversations",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}

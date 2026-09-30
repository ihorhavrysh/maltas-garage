using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaltasGarage.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OneConversationPerPair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add IsSystemNote and OrderRef to ChatMessages
            migrationBuilder.AddColumn<bool>(
                name: "IsSystemNote",
                table: "ChatMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderRef",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            // Merge duplicate conversations: for each (BuyerId, SellerId) pair keep the
            // most recent conversation and reassign all messages from duplicates to it.
            migrationBuilder.Sql(@"
                -- Find the primary conversation per (BuyerId, SellerId): most recent UpdatedAt or CreatedAt
                WITH PrimaryConv AS (
                    SELECT BuyerId, SellerId,
                           MIN(Id) AS PrimaryConvId
                    FROM (
                        SELECT BuyerId, SellerId, Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY BuyerId, SellerId
                                   ORDER BY COALESCE(UpdatedAt, CreatedAt) DESC
                               ) AS rn
                        FROM Conversations
                    ) ranked
                    WHERE rn = 1
                    GROUP BY BuyerId, SellerId
                )
                -- Move messages from duplicate conversations to the primary one
                UPDATE cm
                SET cm.ConversationId = p.PrimaryConvId
                FROM ChatMessages cm
                INNER JOIN Conversations c ON c.Id = cm.ConversationId
                INNER JOIN PrimaryConv p ON p.BuyerId = c.BuyerId AND p.SellerId = c.SellerId
                WHERE cm.ConversationId <> p.PrimaryConvId;
            ");

            migrationBuilder.Sql(@"
                -- Delete duplicate conversations (those not selected as primary)
                WITH PrimaryConv AS (
                    SELECT BuyerId, SellerId,
                           MIN(Id) AS PrimaryConvId
                    FROM (
                        SELECT BuyerId, SellerId, Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY BuyerId, SellerId
                                   ORDER BY COALESCE(UpdatedAt, CreatedAt) DESC
                               ) AS rn
                        FROM Conversations
                    ) ranked
                    WHERE rn = 1
                    GROUP BY BuyerId, SellerId
                )
                DELETE c
                FROM Conversations c
                INNER JOIN PrimaryConv p ON p.BuyerId = c.BuyerId AND p.SellerId = c.SellerId
                WHERE c.Id <> p.PrimaryConvId;
            ");

            // Drop old unique index (BuyerId, SellerId, OrderId)
            migrationBuilder.DropIndex(
                name: "IX_Conversations_BuyerId_SellerId_OrderId",
                table: "Conversations");

            // Create new unique index (BuyerId, SellerId)
            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BuyerId_SellerId",
                table: "Conversations",
                columns: new[] { "BuyerId", "SellerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_BuyerId_SellerId",
                table: "Conversations");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_BuyerId_SellerId_OrderId",
                table: "Conversations",
                columns: new[] { "BuyerId", "SellerId", "OrderId" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "IsSystemNote",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "OrderRef",
                table: "ChatMessages");
        }
    }
}

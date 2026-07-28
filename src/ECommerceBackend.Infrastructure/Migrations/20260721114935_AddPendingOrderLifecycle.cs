using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingOrderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [Orders]
                SET [CancelledAt] = COALESCE(
                        (SELECT MAX([CreatedAt]) FROM [OrderStatusHistories] WHERE [OrderId] = [Orders].[Id] AND [ToStatus] = 4),
                        [OrderDate]),
                    [CancellationReason] = N'LegacyCancellation'
                WHERE [Status] = 4 AND [CancelledAt] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_ExpiresAt_Id",
                table: "Orders",
                columns: new[] { "Status", "ExpiresAt", "Id" },
                filter: "[ExpiresAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_Status_OrderDate",
                table: "Orders",
                columns: new[] { "UserId", "Status", "OrderDate" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Cancellation_Consistent",
                table: "Orders",
                sql: "([Status] = 4 AND (([CancelledAt] IS NOT NULL AND [CancellationReason] IS NOT NULL) OR ([CancelledAt] IS NULL AND [ExpiredAt] IS NULL AND [CancellationReason] IS NULL))) OR ([Status] <> 4 AND [CancelledAt] IS NULL AND [ExpiredAt] IS NULL AND [CancellationReason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Expiration_Consistent",
                table: "Orders",
                sql: "[ExpiredAt] IS NULL OR ([ExpiresAt] IS NOT NULL AND [ExpiredAt] >= [ExpiresAt] AND [Status] = 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ExpiresAt_Valid",
                table: "Orders",
                sql: "[ExpiresAt] IS NULL OR [ExpiresAt] > [OrderDate]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_ExpiresAt_Id",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_Status_OrderDate",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Cancellation_Consistent",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Expiration_Consistent",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ExpiresAt_Valid",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExpiredAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Orders");
        }
    }
}

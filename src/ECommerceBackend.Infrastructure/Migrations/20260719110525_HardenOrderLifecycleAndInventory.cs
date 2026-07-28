using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenOrderLifecycleAndInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderDetails_OrderId",
                table: "OrderDetails");

            migrationBuilder.CreateIndex(
                name: "UX_OrderStatusHistories_OrderId_ToStatus",
                table: "OrderStatusHistories",
                columns: new[] { "OrderId", "ToStatus" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Changed",
                table: "OrderStatusHistories",
                sql: "[FromStatus] IS NULL OR [FromStatus] <> [ToStatus]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories",
                sql: "[ToStatus] BETWEEN 0 AND 4 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 4)");

            migrationBuilder.CreateIndex(
                name: "UX_OrderDetails_OrderId_ProductId",
                table: "OrderDetails",
                columns: new[] { "OrderId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryTransactions_OrderId_ProductId_Type",
                table: "InventoryTransactions",
                columns: new[] { "OrderId", "ProductId", "Type" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] IN (0, 1) AND [OrderId] IS NULL) OR ([Type] IN (2, 3) AND [OrderId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] = 0 AND [QuantityChange] > 0) OR ([Type] = 1 AND [QuantityChange] <> 0) OR ([Type] = 2 AND [QuantityChange] < 0) OR ([Type] = 3 AND [QuantityChange] > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OrderStatusHistories_OrderId_ToStatus",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Changed",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories");

            migrationBuilder.DropIndex(
                name: "UX_OrderDetails_OrderId_ProductId",
                table: "OrderDetails");

            migrationBuilder.DropIndex(
                name: "UX_InventoryTransactions_OrderId_ProductId_Type",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_OrderId",
                table: "OrderDetails",
                column: "OrderId");
        }
    }
}

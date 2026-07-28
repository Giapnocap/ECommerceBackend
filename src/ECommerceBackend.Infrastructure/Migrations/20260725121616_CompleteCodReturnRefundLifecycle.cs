using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCodReturnRefundLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories");

            migrationBuilder.DropIndex(
                name: "UX_OrderStatusHistories_OrderId_ToStatus",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories",
                sql: "[Source] BETWEEN 0 AND 4");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_OrderId_ToStatus",
                table: "OrderStatusHistories",
                columns: new[] { "OrderId", "ToStatus" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories",
                sql: "[ToStatus] BETWEEN 0 AND 6 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders",
                sql: "[Status] BETWEEN 0 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] IN (0, 1) AND [OrderId] IS NULL) OR ([Type] IN (2, 3, 4) AND [OrderId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] = 0 AND [QuantityChange] > 0) OR ([Type] = 1 AND [QuantityChange] <> 0) OR ([Type] = 2 AND [QuantityChange] < 0) OR ([Type] IN (3, 4) AND [QuantityChange] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions",
                sql: "[Type] BETWEEN 0 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_OrderStatusHistories_OrderId_ToStatus",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories",
                sql: "[Source] BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "UX_OrderStatusHistories_OrderId_ToStatus",
                table: "OrderStatusHistories",
                columns: new[] { "OrderId", "ToStatus" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories",
                sql: "[ToStatus] BETWEEN 0 AND 4 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders",
                sql: "[Status] BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] IN (0, 1) AND [OrderId] IS NULL) OR ([Type] IN (2, 3) AND [OrderId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] = 0 AND [QuantityChange] > 0) OR ([Type] = 1 AND [QuantityChange] <> 0) OR ([Type] = 2 AND [QuantityChange] < 0) OR ([Type] = 3 AND [QuantityChange] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions",
                sql: "[Type] BETWEEN 0 AND 3");
        }
    }
}

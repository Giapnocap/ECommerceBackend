using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_IsDeleted_StockQuantity",
                table: "Products",
                columns: new[] { "IsDeleted", "StockQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusHistories_ToStatus_OccurredAt",
                table: "PaymentStatusHistories",
                columns: new[] { "ToStatus", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaidAt",
                table: "Payments",
                column: "PaidAt",
                filter: "[PaidAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_CreatedAt",
                table: "Payments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderDate",
                table: "Orders",
                column: "OrderDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_IsDeleted_StockQuantity",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_PaymentStatusHistories_ToStatus_OccurredAt",
                table: "PaymentStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaidAt",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status_CreatedAt",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderDate",
                table: "Orders");
        }
    }
}

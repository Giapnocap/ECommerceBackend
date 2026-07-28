using ECommerceBackend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260722131500_AddOrderStatusReportingIndex")]
    public partial class AddOrderStatusReportingIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_ToStatus_CreatedAt_OrderId",
                table: "OrderStatusHistories",
                columns: new[] { "ToStatus", "CreatedAt", "OrderId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderStatusHistories_ToStatus_CreatedAt_OrderId",
                table: "OrderStatusHistories");
        }
    }
}

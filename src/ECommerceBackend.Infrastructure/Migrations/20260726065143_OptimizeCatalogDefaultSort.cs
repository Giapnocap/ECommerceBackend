using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    public partial class OptimizeCatalogDefaultSort : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_IsDeleted_CreatedAt_Id",
                table: "Products",
                columns: new[] { "IsDeleted", "CreatedAt", "Id" },
                descending: new[] { false, true, true });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_IsDeleted_CreatedAt_Id",
                table: "Products");
        }
    }
}

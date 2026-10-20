using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    public partial class AddPromotionApplicablePaymentMethod : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApplicablePaymentMethod",
                table: "Promotions",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_ApplicablePaymentMethod_Valid",
                table: "Promotions",
                sql: "[ApplicablePaymentMethod] IS NULL OR [ApplicablePaymentMethod] BETWEEN 0 AND 1");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_ApplicablePaymentMethod_Valid",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "ApplicablePaymentMethod",
                table: "Promotions");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingAndPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<string>(
                name: "PromotionCodeSnapshot",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingMethod",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NormalizedCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinimumSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumDiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsageLimit = table.Column<int>(type: "int", nullable: false),
                    UsageLimitPerCustomer = table.Column<int>(type: "int", nullable: false),
                    UsedCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.CheckConstraint("CK_Promotions_Amounts_Valid", "[MinimumSubtotal] >= 0 AND ([MaximumDiscountAmount] IS NULL OR [MaximumDiscountAmount] > 0)");
                    table.CheckConstraint("CK_Promotions_MaxDiscount_Compatible", "[Type] = 1 OR [MaximumDiscountAmount] IS NULL");
                    table.CheckConstraint("CK_Promotions_Period_Valid", "[StartsAt] < [EndsAt]");
                    table.CheckConstraint("CK_Promotions_Type_Valid", "[Type] BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_Promotions_Usage_Valid", "[UsageLimit] > 0 AND [UsageLimitPerCustomer] > 0 AND [UsedCount] >= 0 AND [UsedCount] <= [UsageLimit]");
                    table.CheckConstraint("CK_Promotions_Value_Valid", "[Value] > 0 AND ([Type] <> 1 OR [Value] <= 100)");
                });

            migrationBuilder.CreateTable(
                name: "PromotionRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionRedemptions", x => x.Id);
                    table.CheckConstraint("CK_PromotionRedemptions_Discount_Positive", "[DiscountAmount] > 0");
                    table.ForeignKey(
                        name: "FK_PromotionRedemptions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionRedemptions_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionRedemptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PromotionId",
                table: "Orders",
                column: "PromotionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Currency_Valid",
                table: "Orders",
                sql: "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_PromotionSnapshot_Consistent",
                table: "Orders",
                sql: "([PromotionId] IS NULL AND [PromotionCodeSnapshot] IS NULL) OR ([PromotionId] IS NOT NULL AND [PromotionCodeSnapshot] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ShippingMethod_Valid",
                table: "Orders",
                sql: "[ShippingMethod] BETWEEN 0 AND 1");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_PromotionId_UserId_CreatedAt",
                table: "PromotionRedemptions",
                columns: new[] { "PromotionId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRedemptions_UserId",
                table: "PromotionRedemptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_PromotionRedemptions_OrderId",
                table: "PromotionRedemptions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_IsActive_StartsAt_EndsAt",
                table: "Promotions",
                columns: new[] { "IsActive", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Promotions_NormalizedCode",
                table: "Promotions",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Promotions_PromotionId",
                table: "Orders",
                column: "PromotionId",
                principalTable: "Promotions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Promotions])
                    OR EXISTS
                    (
                        SELECT 1
                        FROM [Orders]
                        WHERE [PromotionId] IS NOT NULL
                            OR [ShippingMethod] <> 0
                            OR [Currency] <> N'VND'
                    )
                BEGIN
                    THROW 51030, 'Cannot roll back pricing and promotions after new pricing data has been created. Restore the verified backup instead.', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Promotions_PromotionId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "PromotionRedemptions");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PromotionId",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Currency_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_PromotionSnapshot_Consistent",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ShippingMethod_Valid",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PromotionCodeSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PromotionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingMethod",
                table: "Orders");
        }
    }
}

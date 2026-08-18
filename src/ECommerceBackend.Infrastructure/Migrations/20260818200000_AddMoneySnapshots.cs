using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Currency_Valid",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "varchar(3)",
                unicode: false,
                maxLength: 3,
                nullable: false,
                defaultValue: "VND",
                oldClrType: typeof(string),
                oldType: "nvarchar(3)",
                oldMaxLength: 3,
                oldDefaultValue: "VND");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Currency_Valid",
                table: "Orders",
                sql: "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");

            migrationBuilder.AddColumn<string>(
                name: "BaseCurrency",
                table: "Orders",
                type: "varchar(3)",
                unicode: false,
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<decimal>(
                name: "BaseDiscountAmount",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseShippingFee",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseSubtotalAmount",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseTaxAmount",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseTotalAmount",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Orders",
                type: "decimal(18,10)",
                precision: 18,
                scale: 10,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExchangeRateCapturedAt",
                table: "Orders",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "BaseUnitPrice",
                table: "OrderDetails",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [Orders]
                    WHERE [Currency] <> 'VND')
                BEGIN
                    THROW 51000, 'Cannot infer a VND base snapshot for a legacy non-VND order.', 1;
                END;

                UPDATE [Orders]
                SET [BaseCurrency] = 'VND',
                    [BaseSubtotalAmount] = [SubtotalAmount],
                    [BaseDiscountAmount] = [DiscountAmount],
                    [BaseShippingFee] = [ShippingFee],
                    [BaseTaxAmount] = [TaxAmount],
                    [BaseTotalAmount] = [TotalAmount],
                    [ExchangeRate] = 1,
                    [ExchangeRateCapturedAt] = [OrderDate];

                UPDATE [OrderDetails]
                SET [BaseUnitPrice] = [UnitPrice];
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_BaseAmounts_NonNegative",
                table: "Orders",
                sql: "[BaseSubtotalAmount] >= 0 AND [BaseDiscountAmount] >= 0 AND [BaseShippingFee] >= 0 AND [BaseTaxAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_BaseCurrency_Valid",
                table: "Orders",
                sql: "LEN([BaseCurrency]) = 3 AND [BaseCurrency] = UPPER([BaseCurrency])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_BaseTotalAmount_Consistent",
                table: "Orders",
                sql: "[BaseTotalAmount] = [BaseSubtotalAmount] - [BaseDiscountAmount] + [BaseShippingFee] + [BaseTaxAmount] AND [BaseTotalAmount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ExchangeRate_Valid",
                table: "Orders",
                sql: "[ExchangeRate] > 0 AND [ExchangeRate] <= 1000000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ExchangeRateCapturedAt_Valid",
                table: "Orders",
                sql: "[ExchangeRateCapturedAt] <= DATEADD(minute, 5, [OrderDate])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_SameCurrencySnapshot_Consistent",
                table: "Orders",
                sql: "[BaseCurrency] <> [Currency] OR ([ExchangeRate] = 1 AND [BaseSubtotalAmount] = [SubtotalAmount] AND [BaseDiscountAmount] = [DiscountAmount] AND [BaseShippingFee] = [ShippingFee] AND [BaseTaxAmount] = [TaxAmount] AND [BaseTotalAmount] = [TotalAmount])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderDetails_BaseUnitPrice_Positive",
                table: "OrderDetails",
                sql: "[BaseUnitPrice] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [Orders]
                    WHERE [BaseCurrency] <> [Currency]
                       OR [ExchangeRate] <> 1)
                BEGIN
                    THROW 51000, 'Rollback would discard multi-currency order snapshots.', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_BaseAmounts_NonNegative",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_BaseCurrency_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_BaseTotalAmount_Consistent",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ExchangeRate_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ExchangeRateCapturedAt_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_SameCurrencySnapshot_Consistent",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderDetails_BaseUnitPrice_Positive",
                table: "OrderDetails");

            migrationBuilder.DropColumn(
                name: "BaseCurrency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BaseDiscountAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BaseShippingFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BaseSubtotalAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BaseTaxAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BaseTotalAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeRateCapturedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BaseUnitPrice",
                table: "OrderDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Currency_Valid",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND",
                oldClrType: typeof(string),
                oldType: "varchar(3)",
                oldUnicode: false,
                oldMaxLength: 3,
                oldDefaultValue: "VND");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Currency_Valid",
                table: "Orders",
                sql: "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");
        }
    }
}

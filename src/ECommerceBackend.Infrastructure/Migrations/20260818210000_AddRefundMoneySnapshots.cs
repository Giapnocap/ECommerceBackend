using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundMoneySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseAmount",
                table: "PaymentRefunds",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseCurrency",
                table: "PaymentRefunds",
                type: "varchar(3)",
                unicode: false,
                maxLength: 3,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE refund
                SET
                    [BaseCurrency] = [order].[BaseCurrency],
                    [BaseAmount] = CASE
                        WHEN refund.[Currency] = [order].[BaseCurrency]
                            THEN refund.[Amount]
                        WHEN refund.[Amount] = payment.[Amount]
                            THEN [order].[BaseTotalAmount]
                        ELSE ROUND(refund.[Amount] / NULLIF([order].[ExchangeRate], 0), 0)
                    END
                FROM [PaymentRefunds] AS refund
                INNER JOIN [Payments] AS payment ON payment.[Id] = refund.[PaymentId]
                INNER JOIN [Orders] AS [order] ON [order].[Id] = payment.[OrderId];

                IF EXISTS (
                    SELECT 1
                    FROM [PaymentRefunds]
                    WHERE [BaseAmount] IS NULL
                        OR [BaseAmount] <= 0
                        OR [BaseCurrency] IS NULL)
                BEGIN
                    THROW 51000, 'Unable to backfill payment refund money snapshots.', 1;
                END;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "BaseAmount",
                table: "PaymentRefunds",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BaseCurrency",
                table: "PaymentRefunds",
                type: "varchar(3)",
                unicode: false,
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(3)",
                oldUnicode: false,
                oldMaxLength: 3,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentRefunds_BaseAmount_Positive",
                table: "PaymentRefunds",
                sql: "[BaseAmount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentRefunds_BaseCurrency_Format",
                table: "PaymentRefunds",
                sql: "LEN([BaseCurrency]) = 3 AND [BaseCurrency] = UPPER([BaseCurrency])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentRefunds_BaseAmount_Positive",
                table: "PaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentRefunds_BaseCurrency_Format",
                table: "PaymentRefunds");

            migrationBuilder.DropColumn(
                name: "BaseAmount",
                table: "PaymentRefunds");

            migrationBuilder.DropColumn(
                name: "BaseCurrency",
                table: "PaymentRefunds");
        }
    }
}

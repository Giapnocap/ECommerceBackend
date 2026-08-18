using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStripePaymentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentStatusHistories_Status_Valid",
                table: "PaymentStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Method_Valid",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_PaidAt_MatchesStatus",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Status_Valid",
                table: "Payments");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Payments",
                type: "varchar(3)",
                unicode: false,
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                "UPDATE [Payments] SET [RefundedAmount] = [Amount] WHERE [Status] = 4;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                table: "PaymentWebhookEvents",
                sql: "[ResultingStatus] BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentStatusHistories_Status_Valid",
                table: "PaymentStatusHistories",
                sql: "[ToStatus] BETWEEN 0 AND 7 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Currency_Format",
                table: "Payments",
                sql: "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Method_Valid",
                table: "Payments",
                sql: "[Method] BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_PaidAt_MatchesStatus",
                table: "Payments",
                sql: "([Status] IN (1, 4, 7) AND [PaidAt] IS NOT NULL) OR ([Status] IN (0, 2, 3, 5, 6) AND [PaidAt] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_RefundedAmount_Valid",
                table: "Payments",
                sql: "([Status] = 4 AND [RefundedAmount] = [Amount]) OR ([Status] = 7 AND [RefundedAmount] > 0 AND [RefundedAmount] < [Amount]) OR ([Status] NOT IN (4, 7) AND [RefundedAmount] = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Status_Valid",
                table: "Payments",
                sql: "[Status] BETWEEN 0 AND 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [Payments] WHERE [Method] > 0 OR [Status] > 4) "
                + "THROW 51000, 'Cannot roll back Stripe payment foundation while online payment data exists.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentStatusHistories_Status_Valid",
                table: "PaymentStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Currency_Format",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Method_Valid",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_PaidAt_MatchesStatus",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_RefundedAmount_Valid",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Status_Valid",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "Payments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                table: "PaymentWebhookEvents",
                sql: "[ResultingStatus] BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentStatusHistories_Status_Valid",
                table: "PaymentStatusHistories",
                sql: "[ToStatus] BETWEEN 0 AND 4 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Method_Valid",
                table: "Payments",
                sql: "[Method] BETWEEN 0 AND 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_PaidAt_MatchesStatus",
                table: "Payments",
                sql: "([Status] IN (1, 4) AND [PaidAt] IS NOT NULL) OR ([Status] IN (0, 2, 3) AND [PaidAt] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Status_Valid",
                table: "Payments",
                sql: "[Status] BETWEEN 0 AND 4");
        }
    }
}

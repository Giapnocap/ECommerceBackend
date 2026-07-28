using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenPaymentLifecycleAndWebhookAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAt",
                table: "PaymentWebhookEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultingStatus",
                table: "PaymentWebhookEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StatusChanged",
                table: "PaymentWebhookEvents",
                type: "bit",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE webhook
                SET [OccurredAt] = webhook.[ProcessedAt],
                    [ResultingStatus] = CASE LOWER(JSON_VALUE(webhook.[Payload], '$.status'))
                        WHEN 'paid' THEN 1
                        WHEN 'failed' THEN 2
                        WHEN 'cancelled' THEN 3
                        WHEN 'refunded' THEN 4
                        ELSE payment.[Status]
                    END,
                    [StatusChanged] = 1
                FROM [PaymentWebhookEvents] webhook
                INNER JOIN [Payments] payment ON payment.[Id] = webhook.[PaymentId];
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "OccurredAt",
                table: "PaymentWebhookEvents",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ResultingStatus",
                table: "PaymentWebhookEvents",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "StatusChanged",
                table: "PaymentWebhookEvents",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_PaymentStatusHistories_Source_Valid", "[Source] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_PaymentStatusHistories_Status_Changed", "[FromStatus] IS NULL OR [FromStatus] <> [ToStatus]");
                    table.CheckConstraint("CK_PaymentStatusHistories_Status_Valid", "[ToStatus] BETWEEN 0 AND 4 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 4)");
                    table.ForeignKey(
                        name: "FK_PaymentStatusHistories_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentStatusHistories_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [PaymentStatusHistories]
                    ([Id], [PaymentId], [ChangedByUserId], [FromStatus], [ToStatus], [Source], [Reference], [OccurredAt], [CreatedAt])
                SELECT NEWID(), payment.[Id], NULL, NULL, payment.[Status], 3, NULL,
                    COALESCE(payment.[PaidAt], payment.[CreatedAt]), SYSUTCDATETIME()
                FROM [Payments] payment;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentWebhookEvents_PayloadHash_Length",
                table: "PaymentWebhookEvents",
                sql: "LEN([PayloadHash]) = 64");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                table: "PaymentWebhookEvents",
                sql: "[ResultingStatus] BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_PaidAt_MatchesStatus",
                table: "Payments",
                sql: "([Status] IN (1, 4) AND [PaidAt] IS NOT NULL) OR ([Status] IN (0, 2, 3) AND [PaidAt] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusHistories_ChangedByUserId",
                table: "PaymentStatusHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusHistories_PaymentId_CreatedAt",
                table: "PaymentStatusHistories",
                columns: new[] { "PaymentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_PaymentStatusHistories_PaymentId_ToStatus",
                table: "PaymentStatusHistories",
                columns: new[] { "PaymentId", "ToStatus" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentWebhookEvents_PayloadHash_Length",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_PaidAt_MatchesStatus",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "ResultingStatus",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "StatusChanged",
                table: "PaymentWebhookEvents");
        }
    }
}

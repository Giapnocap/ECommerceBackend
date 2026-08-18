using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PaymentStatusHistories_PaymentId_ToStatus",
                table: "PaymentStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories");

            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "PaymentWebhookEvents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalCreatedAt",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCreationIdempotencyKey",
                table: "Payments",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalCreationLeaseUntil",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastProviderEventAt",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProviderRefundId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessingLeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.CheckConstraint("CK_PaymentRefunds_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_PaymentRefunds_AttemptCount_Valid", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_PaymentRefunds_Currency_Format", "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");
                    table.CheckConstraint("CK_PaymentRefunds_Status_Valid", "[Status] BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.Sql(
                "UPDATE [Payments] SET "
                + "[ExternalCreationIdempotencyKey] = 'payment-' + LOWER(REPLACE(CONVERT(varchar(36), [Id]), '-', '')), "
                + "[ExternalCreatedAt] = CASE WHEN [ProviderTransactionId] IS NOT NULL THEN [CreatedAt] ELSE NULL END "
                + "WHERE [Method] = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusHistories_PaymentId_ToStatus",
                table: "PaymentStatusHistories",
                columns: new[] { "PaymentId", "ToStatus" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories",
                sql: "[Source] BETWEEN 0 AND 6");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ExternalCreationIdempotencyKey",
                table: "Payments",
                column: "ExternalCreationIdempotencyKey",
                unique: true,
                filter: "[ExternalCreationIdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ExternalCreationLeaseUntil",
                table: "Payments",
                column: "ExternalCreationLeaseUntil",
                filter: "[ExternalCreationLeaseUntil] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_LastProviderEventAt",
                table: "Payments",
                column: "LastProviderEventAt",
                filter: "[LastProviderEventAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PaymentId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "PaymentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_ProviderRefundId",
                table: "PaymentRefunds",
                column: "ProviderRefundId",
                unique: true,
                filter: "[ProviderRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_RequestedByUserId",
                table: "PaymentRefunds",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_Status_ProcessingLeaseUntil",
                table: "PaymentRefunds",
                columns: new[] { "Status", "ProcessingLeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [PaymentRefunds]) "
                + "OR EXISTS (SELECT 1 FROM [PaymentStatusHistories] WHERE [Source] > 4) "
                + "OR EXISTS (SELECT 1 FROM [PaymentStatusHistories] GROUP BY [PaymentId], [ToStatus] HAVING COUNT(*) > 1) "
                + "THROW 51000, 'Cannot roll back payment reliability while online payment history exists.', 1;");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropIndex(
                name: "IX_PaymentStatusHistories_PaymentId_ToStatus",
                table: "PaymentStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ExternalCreationIdempotencyKey",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ExternalCreationLeaseUntil",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_LastProviderEventAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "ExternalCreatedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalCreationIdempotencyKey",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalCreationLeaseUntil",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "LastProviderEventAt",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "UX_PaymentStatusHistories_PaymentId_ToStatus",
                table: "PaymentStatusHistories",
                columns: new[] { "PaymentId", "ToStatus" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentStatusHistories_Source_Valid",
                table: "PaymentStatusHistories",
                sql: "[Source] BETWEEN 0 AND 4");
        }
    }
}

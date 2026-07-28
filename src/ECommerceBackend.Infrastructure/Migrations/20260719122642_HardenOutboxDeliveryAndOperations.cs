using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenOutboxDeliveryAndOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedAt_DeadLetteredAt_NextAttemptAt_LockedAt",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "OutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_DeadLetteredAt",
                table: "OutboxMessages",
                column: "DeadLetteredAt",
                filter: "[DeadLetteredAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Ready",
                table: "OutboxMessages",
                columns: new[] { "NextAttemptAt", "LockedAt", "OccurredAt" },
                filter: "[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");

            migrationBuilder.Sql("""
                UPDATE [OutboxMessages]
                SET [DeadLetteredAt] = NULL
                WHERE [ProcessedAt] IS NOT NULL AND [DeadLetteredAt] IS NOT NULL;

                UPDATE [OutboxMessages]
                SET [LastAttemptAt] = COALESCE([LockedAt], [OccurredAt])
                WHERE [Attempts] > 0 AND [LastAttemptAt] IS NULL;

                UPDATE [OutboxMessages]
                SET [LockId] = NULL, [LockedAt] = NULL
                WHERE [ProcessedAt] IS NOT NULL OR [DeadLetteredAt] IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_TerminalState_Exclusive",
                table: "OutboxMessages",
                sql: "[ProcessedAt] IS NULL OR [DeadLetteredAt] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_TerminalState_Unlocked",
                table: "OutboxMessages",
                sql: "([ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL) OR ([LockId] IS NULL AND [LockedAt] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_DeadLetteredAt",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Ready",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_TerminalState_Exclusive",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_TerminalState_Unlocked",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_DeadLetteredAt_NextAttemptAt_LockedAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "DeadLetteredAt", "NextAttemptAt", "LockedAt" },
                filter: "[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");
        }
    }
}

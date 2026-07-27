using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.HasIndex(message => new
            {
                message.NextAttemptAt,
                message.LockedAt,
                message.OccurredAt
            })
                .HasDatabaseName("IX_OutboxMessages_Ready")
                .HasFilter("[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");
            builder.HasIndex(message => message.DeadLetteredAt)
                .HasDatabaseName("IX_OutboxMessages_DeadLetteredAt")
                .HasFilter("[DeadLetteredAt] IS NOT NULL");
            builder.HasIndex(message => message.ProcessedAt)
                .HasDatabaseName("IX_OutboxMessages_ProcessedAt")
                .HasFilter("[ProcessedAt] IS NOT NULL");
            builder.Property(message => message.Type).HasMaxLength(200);
            builder.Property(message => message.LastError).HasMaxLength(2000);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_OutboxMessages_Attempts_NonNegative",
                    "[Attempts] >= 0");
                table.HasCheckConstraint(
                    "CK_OutboxMessages_Lock_Consistent",
                    "([LockId] IS NULL AND [LockedAt] IS NULL) OR ([LockId] IS NOT NULL AND [LockedAt] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_OutboxMessages_TerminalState_Exclusive",
                    "[ProcessedAt] IS NULL OR [DeadLetteredAt] IS NULL");
                table.HasCheckConstraint(
                    "CK_OutboxMessages_TerminalState_Unlocked",
                    "([ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL) OR ([LockId] IS NULL AND [LockedAt] IS NULL)");
            });
        }
    }

    internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
    {
        public void Configure(EntityTypeBuilder<AuditEvent> builder)
        {
            builder.HasIndex(audit => new { audit.CreatedAt, audit.Id });
            builder.HasIndex(audit => new { audit.ActorUserId, audit.CreatedAt });
            builder.HasIndex(audit => new
            {
                audit.EntityType,
                audit.EntityId,
                audit.CreatedAt
            });
            builder.HasIndex(audit => audit.CorrelationId);
            builder.Property(audit => audit.Action).HasMaxLength(100);
            builder.Property(audit => audit.EntityType).HasMaxLength(100);
            builder.Property(audit => audit.EntityId).HasMaxLength(100);
            builder.Property(audit => audit.CorrelationId).HasMaxLength(128);
            builder.Property(audit => audit.IpAddress).HasMaxLength(45);
            builder.Property(audit => audit.MetadataJson).HasMaxLength(4000);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_AuditEvents_Action_NotEmpty",
                    "LEN([Action]) > 0");
                table.HasCheckConstraint(
                    "CK_AuditEvents_EntityType_NotEmpty",
                    "LEN([EntityType]) > 0");
                table.HasCheckConstraint(
                    "CK_AuditEvents_CorrelationId_NotEmpty",
                    "LEN([CorrelationId]) > 0");
            });
        }
    }
}

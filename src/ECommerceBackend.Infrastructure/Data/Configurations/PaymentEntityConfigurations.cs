using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
    {
        public void Configure(EntityTypeBuilder<Payment> builder)
        {
            builder.Property(payment => payment.Amount).HasPrecision(18, 2);
            builder.Property(payment => payment.RowVersion).IsRowVersion();
            builder.HasIndex(payment => payment.OrderId).IsUnique();
            builder.HasIndex(payment => new { payment.Status, payment.CreatedAt });
            builder.HasIndex(payment => payment.PaidAt)
                .HasFilter("[PaidAt] IS NOT NULL");
            builder.HasIndex(payment => new
            {
                payment.Provider,
                payment.ProviderTransactionId
            })
                .HasFilter("[Provider] IS NOT NULL AND [ProviderTransactionId] IS NOT NULL")
                .IsUnique();
            builder.Property(payment => payment.Provider).HasMaxLength(100);
            builder.Property(payment => payment.ProviderTransactionId).HasMaxLength(200);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0");
                table.HasCheckConstraint("CK_Payments_Method_Valid", "[Method] BETWEEN 0 AND 0");
                table.HasCheckConstraint("CK_Payments_Status_Valid", "[Status] BETWEEN 0 AND 4");
                table.HasCheckConstraint(
                    "CK_Payments_PaidAt_MatchesStatus",
                    "([Status] IN (1, 4) AND [PaidAt] IS NOT NULL) OR ([Status] IN (0, 2, 3) AND [PaidAt] IS NULL)");
            });

            builder.HasOne(payment => payment.Order)
                .WithOne(order => order.Payment)
                .HasForeignKey<Payment>(payment => payment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    internal sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
    {
        public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
        {
            builder.HasIndex(webhook => new { webhook.Provider, webhook.ProviderEventId })
                .IsUnique();
            builder.HasIndex(webhook => new { webhook.PaymentId, webhook.ReceivedAt });
            builder.HasIndex(webhook => webhook.ReceivedAt)
                .HasDatabaseName("IX_PaymentWebhookEvents_ReceivedAt");
            builder.Property(webhook => webhook.Provider).HasMaxLength(100);
            builder.Property(webhook => webhook.ProviderEventId).HasMaxLength(200);
            builder.Property(webhook => webhook.PayloadHash).HasMaxLength(64);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                    "[ResultingStatus] BETWEEN 0 AND 4");
                table.HasCheckConstraint(
                    "CK_PaymentWebhookEvents_PayloadHash_Length",
                    "LEN([PayloadHash]) = 64");
            });

            builder.HasOne(webhook => webhook.Payment)
                .WithMany(payment => payment.WebhookEvents)
                .HasForeignKey(webhook => webhook.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    internal sealed class PaymentStatusHistoryConfiguration : IEntityTypeConfiguration<PaymentStatusHistory>
    {
        public void Configure(EntityTypeBuilder<PaymentStatusHistory> builder)
        {
            builder.HasIndex(history => new { history.PaymentId, history.CreatedAt });
            builder.HasIndex(history => new { history.ToStatus, history.OccurredAt });
            builder.HasIndex(history => new { history.PaymentId, history.ToStatus })
                .HasDatabaseName("UX_PaymentStatusHistories_PaymentId_ToStatus")
                .IsUnique();
            builder.Property(history => history.Reference).HasMaxLength(200);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PaymentStatusHistories_Status_Valid",
                    "[ToStatus] BETWEEN 0 AND 4 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 4)");
                table.HasCheckConstraint(
                    "CK_PaymentStatusHistories_Status_Changed",
                    "[FromStatus] IS NULL OR [FromStatus] <> [ToStatus]");
                table.HasCheckConstraint(
                    "CK_PaymentStatusHistories_Source_Valid",
                    "[Source] BETWEEN 0 AND 4");
            });

            builder.HasOne(history => history.Payment)
                .WithMany(payment => payment.StatusHistory)
                .HasForeignKey(history => history.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(history => history.ChangedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}

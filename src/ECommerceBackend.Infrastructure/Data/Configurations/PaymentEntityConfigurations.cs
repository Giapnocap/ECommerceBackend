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
            builder.Property(payment => payment.RefundedAmount).HasPrecision(18, 2);
            builder.Property(payment => payment.Currency)
                .HasMaxLength(3)
                .IsUnicode(false);
            builder.Property(payment => payment.RowVersion).IsRowVersion();
            builder.HasIndex(payment => payment.OrderId).IsUnique();
            builder.HasIndex(payment => new { payment.Status, payment.CreatedAt });
            builder.HasIndex(payment => payment.PaidAt)
                .HasFilter("[PaidAt] IS NOT NULL");
            builder.HasIndex(payment => payment.ExternalCreationIdempotencyKey)
                .HasFilter("[ExternalCreationIdempotencyKey] IS NOT NULL")
                .IsUnique();
            builder.HasIndex(payment => payment.ExternalCreationLeaseUntil)
                .HasFilter("[ExternalCreationLeaseUntil] IS NOT NULL");
            builder.HasIndex(payment => payment.LastProviderEventAt)
                .HasFilter("[LastProviderEventAt] IS NOT NULL");
            builder.HasIndex(payment => new
            {
                payment.Status,
                payment.LastReconciledAt
            })
                .HasFilter("[ProviderTransactionId] IS NOT NULL");
            builder.HasIndex(payment => new
            {
                payment.Provider,
                payment.ProviderTransactionId
            })
                .HasFilter("[Provider] IS NOT NULL AND [ProviderTransactionId] IS NOT NULL")
                .IsUnique();
            builder.Property(payment => payment.Provider).HasMaxLength(100);
            builder.Property(payment => payment.ProviderTransactionId).HasMaxLength(200);
            builder.Property(payment => payment.ExternalCreationIdempotencyKey)
                .HasMaxLength(100)
                .IsUnicode(false);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0");
                table.HasCheckConstraint("CK_Payments_Method_Valid", "[Method] BETWEEN 0 AND 1");
                table.HasCheckConstraint("CK_Payments_Status_Valid", "[Status] BETWEEN 0 AND 7");
                table.HasCheckConstraint(
                    "CK_Payments_Currency_Format",
                    "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");
                table.HasCheckConstraint(
                    "CK_Payments_RefundedAmount_Valid",
                    "([Status] = 4 AND [RefundedAmount] = [Amount]) OR "
                    + "([Status] = 7 AND [RefundedAmount] > 0 AND [RefundedAmount] < [Amount]) OR "
                    + "([Status] NOT IN (4, 7) AND [RefundedAmount] = 0)");
                table.HasCheckConstraint(
                    "CK_Payments_PaidAt_MatchesStatus",
                    "([Status] IN (1, 4, 7) AND [PaidAt] IS NOT NULL) OR ([Status] IN (0, 2, 3, 5, 6) AND [PaidAt] IS NULL)");
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
            builder.Property(webhook => webhook.EventType).HasMaxLength(100);
            builder.Property(webhook => webhook.PayloadHash).HasMaxLength(64);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PaymentWebhookEvents_ResultingStatus_Valid",
                    "[ResultingStatus] BETWEEN 0 AND 7");
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
                .HasDatabaseName("IX_PaymentStatusHistories_PaymentId_ToStatus");
            builder.Property(history => history.Reference).HasMaxLength(200);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PaymentStatusHistories_Status_Valid",
                    "[ToStatus] BETWEEN 0 AND 7 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 7)");
                table.HasCheckConstraint(
                    "CK_PaymentStatusHistories_Status_Changed",
                    "[FromStatus] IS NULL OR [FromStatus] <> [ToStatus]");
                table.HasCheckConstraint(
                    "CK_PaymentStatusHistories_Source_Valid",
                    "[Source] BETWEEN 0 AND 6");
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

    internal sealed class PaymentRefundConfiguration
        : IEntityTypeConfiguration<PaymentRefund>
    {
        public void Configure(EntityTypeBuilder<PaymentRefund> builder)
        {
            builder.Property(refund => refund.Amount).HasPrecision(18, 2);
            builder.Property(refund => refund.BaseAmount).HasPrecision(18, 2);
            builder.Property(refund => refund.Currency)
                .HasMaxLength(3)
                .IsUnicode(false);
            builder.Property(refund => refund.BaseCurrency)
                .HasMaxLength(3)
                .IsUnicode(false);
            builder.Property(refund => refund.IdempotencyKey)
                .HasMaxLength(200);
            builder.Property(refund => refund.ProviderRefundId)
                .HasMaxLength(200);
            builder.Property(refund => refund.FailureCode)
                .HasMaxLength(100)
                .IsUnicode(false);
            builder.Property(refund => refund.RowVersion).IsRowVersion();

            builder.HasIndex(refund => new
            {
                refund.PaymentId,
                refund.IdempotencyKey
            }).IsUnique();
            builder.HasIndex(refund => refund.ProviderRefundId)
                .HasFilter("[ProviderRefundId] IS NOT NULL")
                .IsUnique();
            builder.HasIndex(refund => new
            {
                refund.Status,
                refund.ProcessingLeaseUntil
            });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PaymentRefunds_Amount_Positive",
                    "[Amount] > 0");
                table.HasCheckConstraint(
                    "CK_PaymentRefunds_BaseAmount_Positive",
                    "[BaseAmount] > 0");
                table.HasCheckConstraint(
                    "CK_PaymentRefunds_BaseCurrency_Format",
                    "LEN([BaseCurrency]) = 3 AND [BaseCurrency] = UPPER([BaseCurrency])");
                table.HasCheckConstraint(
                    "CK_PaymentRefunds_Currency_Format",
                    "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");
                table.HasCheckConstraint(
                    "CK_PaymentRefunds_Status_Valid",
                    "[Status] BETWEEN 0 AND 4");
                table.HasCheckConstraint(
                    "CK_PaymentRefunds_AttemptCount_Valid",
                    "[AttemptCount] >= 0");
            });

            builder.HasOne(refund => refund.Payment)
                .WithMany(payment => payment.Refunds)
                .HasForeignKey(refund => refund.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(refund => refund.RequestedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}

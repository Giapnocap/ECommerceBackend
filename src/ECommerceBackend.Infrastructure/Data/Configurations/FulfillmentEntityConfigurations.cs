using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class ShipmentConfiguration
        : IEntityTypeConfiguration<Shipment>
    {
        public void Configure(EntityTypeBuilder<Shipment> builder)
        {
            builder.Property(shipment => shipment.Carrier).HasMaxLength(100);
            builder.Property(shipment => shipment.TrackingNumber).HasMaxLength(100);
            builder.Property(shipment => shipment.RowVersion).IsRowVersion();

            builder.HasIndex(shipment => shipment.OrderId)
                .HasDatabaseName("UX_Shipments_OrderId")
                .IsUnique();
            builder.HasIndex(shipment => new
            {
                shipment.Carrier,
                shipment.TrackingNumber
            })
                .HasDatabaseName("UX_Shipments_Carrier_TrackingNumber")
                .IsUnique();

            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Shipments_DeliveryTime_Valid",
                    "[DeliveredAt] IS NULL OR [DeliveredAt] >= [ShippedAt]");
                table.HasCheckConstraint(
                    "CK_Shipments_Carrier_NotBlank",
                    "LEN(LTRIM(RTRIM([Carrier]))) > 0");
                table.HasCheckConstraint(
                    "CK_Shipments_TrackingNumber_NotBlank",
                    "LEN(LTRIM(RTRIM([TrackingNumber]))) > 0");
            });

            builder.HasOne(shipment => shipment.Order)
                .WithOne(order => order.Shipment)
                .HasForeignKey<Shipment>(shipment => shipment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(shipment => shipment.CreatedByUser)
                .WithMany()
                .HasForeignKey(shipment => shipment.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    internal sealed class ReturnRequestConfiguration
        : IEntityTypeConfiguration<ReturnRequest>
    {
        public void Configure(EntityTypeBuilder<ReturnRequest> builder)
        {
            builder.Property(returnRequest => returnRequest.Reason)
                .HasMaxLength(500);
            builder.Property(returnRequest => returnRequest.ReviewNote)
                .HasMaxLength(500);
            builder.Property(returnRequest => returnRequest.InspectionNote)
                .HasMaxLength(500);
            builder.Property(returnRequest => returnRequest.RowVersion)
                .IsRowVersion();

            builder.HasIndex(returnRequest => returnRequest.OrderId)
                .HasDatabaseName("UX_ReturnRequests_OrderId")
                .IsUnique();
            builder.HasIndex(returnRequest => new
            {
                returnRequest.Status,
                returnRequest.RequestedAt,
                returnRequest.Id
            });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_ReturnRequests_Status_Valid",
                    "[Status] BETWEEN 0 AND 4");
                table.HasCheckConstraint(
                    "CK_ReturnRequests_Reason_NotBlank",
                    "LEN(LTRIM(RTRIM([Reason]))) > 0");
                table.HasCheckConstraint(
                    "CK_ReturnRequests_Review_Consistent",
                    "([Status] = 0 AND [ReviewedAt] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewNote] IS NULL) OR ([Status] BETWEEN 1 AND 4 AND [ReviewedAt] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_ReturnRequests_Receipt_Consistent",
                    "([Status] BETWEEN 0 AND 2 AND [ReceivedAt] IS NULL AND [ReceivedByUserId] IS NULL AND [InspectionNote] IS NULL) OR ([Status] BETWEEN 3 AND 4 AND [ReceivedAt] IS NOT NULL AND [InspectionNote] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_ReturnRequests_Refund_Consistent",
                    "([Status] < 4 AND [RefundedAt] IS NULL) OR ([Status] = 4 AND [RefundedAt] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_ReturnRequests_Timeline_Valid",
                    "([ReviewedAt] IS NULL OR [ReviewedAt] >= [RequestedAt]) AND ([ReceivedAt] IS NULL OR [ReceivedAt] >= [ReviewedAt]) AND ([RefundedAt] IS NULL OR [RefundedAt] >= [ReceivedAt])");
            });

            builder.HasOne(returnRequest => returnRequest.Order)
                .WithOne(order => order.ReturnRequest)
                .HasForeignKey<ReturnRequest>(
                    returnRequest => returnRequest.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(returnRequest => returnRequest.RequestedByUser)
                .WithMany()
                .HasForeignKey(returnRequest => returnRequest.RequestedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(returnRequest => returnRequest.ReviewedByUser)
                .WithMany()
                .HasForeignKey(returnRequest => returnRequest.ReviewedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(returnRequest => returnRequest.ReceivedByUser)
                .WithMany()
                .HasForeignKey(returnRequest => returnRequest.ReceivedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFulfillmentAndReturnWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders");

            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InspectionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequests", x => x.Id);
                    table.CheckConstraint("CK_ReturnRequests_Reason_NotBlank", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_ReturnRequests_Receipt_Consistent", "([Status] BETWEEN 0 AND 2 AND [ReceivedAt] IS NULL AND [ReceivedByUserId] IS NULL AND [InspectionNote] IS NULL) OR ([Status] BETWEEN 3 AND 4 AND [ReceivedAt] IS NOT NULL AND [InspectionNote] IS NOT NULL)");
                    table.CheckConstraint("CK_ReturnRequests_Refund_Consistent", "([Status] < 4 AND [RefundedAt] IS NULL) OR ([Status] = 4 AND [RefundedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_ReturnRequests_Review_Consistent", "([Status] = 0 AND [ReviewedAt] IS NULL AND [ReviewedByUserId] IS NULL AND [ReviewNote] IS NULL) OR ([Status] BETWEEN 1 AND 4 AND [ReviewedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_ReturnRequests_Status_Valid", "[Status] BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_ReturnRequests_Timeline_Valid", "([ReviewedAt] IS NULL OR [ReviewedAt] >= [RequestedAt]) AND ([ReceivedAt] IS NULL OR [ReceivedAt] >= [ReviewedAt]) AND ([RefundedAt] IS NULL OR [RefundedAt] >= [ReceivedAt])");
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Users_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.CheckConstraint("CK_Shipments_Carrier_NotBlank", "LEN(LTRIM(RTRIM([Carrier]))) > 0");
                    table.CheckConstraint("CK_Shipments_DeliveryTime_Valid", "[DeliveredAt] IS NULL OR [DeliveredAt] >= [ShippedAt]");
                    table.CheckConstraint("CK_Shipments_TrackingNumber_NotBlank", "LEN(LTRIM(RTRIM([TrackingNumber]))) > 0");
                    table.ForeignKey(
                        name: "FK_Shipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Shipments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories",
                sql: "[ToStatus] BETWEEN 0 AND 9 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 9)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders",
                sql: "[Status] BETWEEN 0 AND 9");

            migrationBuilder.Sql(
                """
                INSERT INTO [Shipments]
                    ([Id], [OrderId], [Carrier], [TrackingNumber],
                     [CreatedByUserId], [ShippedAt], [DeliveredAt])
                SELECT
                    NEWID(),
                    [order].[Id],
                    N'Legacy',
                    CONCAT(N'LEGACY-', [order].[OrderNumber]),
                    NULL,
                    [timeline].[ShippedAt],
                    CASE
                        WHEN [order].[Status] IN (3, 6)
                            THEN CASE
                                WHEN [timeline].[DeliveredAt] < [timeline].[ShippedAt]
                                    THEN [timeline].[ShippedAt]
                                ELSE [timeline].[DeliveredAt]
                            END
                        ELSE NULL
                    END
                FROM [Orders] AS [order]
                CROSS APPLY
                (
                    SELECT
                        COALESCE(
                            (SELECT MIN([history].[CreatedAt])
                             FROM [OrderStatusHistories] AS [history]
                             WHERE [history].[OrderId] = [order].[Id]
                               AND [history].[ToStatus] = 2),
                            [order].[OrderDate]) AS [ShippedAt],
                        COALESCE(
                            (SELECT MIN([history].[CreatedAt])
                             FROM [OrderStatusHistories] AS [history]
                             WHERE [history].[OrderId] = [order].[Id]
                               AND [history].[ToStatus] = 3),
                            [order].[OrderDate]) AS [DeliveredAt]
                ) AS [timeline]
                WHERE [order].[Status] IN (2, 3, 6);

                INSERT INTO [ReturnRequests]
                    ([Id], [OrderId], [RequestedByUserId], [Reason], [Status],
                     [RequestedAt], [ReviewedByUserId], [ReviewedAt],
                     [ReviewNote], [ReceivedByUserId], [ReceivedAt],
                     [InspectionNote], [RefundedAt])
                SELECT
                    NEWID(),
                    [order].[Id],
                    [order].[UserId],
                    N'Dữ liệu hoàn hàng trước khi áp dụng quy trình yêu cầu trả hàng',
                    CASE WHEN [payment].[Status] = 4 THEN 4 ELSE 3 END,
                    [shipment].[DeliveredAt],
                    NULL,
                    [shipment].[DeliveredAt],
                    N'Được tạo khi nâng cấp dữ liệu',
                    NULL,
                    [timeline].[ReceivedAt],
                    N'Hàng hoàn đã được ghi nhận trước khi nâng cấp dữ liệu',
                    CASE
                        WHEN [payment].[Status] = 4
                            THEN CASE
                                WHEN [timeline].[RefundedAt] < [timeline].[ReceivedAt]
                                    THEN [timeline].[ReceivedAt]
                                ELSE [timeline].[RefundedAt]
                            END
                        ELSE NULL
                    END
                FROM [Orders] AS [order]
                INNER JOIN [Shipments] AS [shipment]
                    ON [shipment].[OrderId] = [order].[Id]
                LEFT JOIN [Payments] AS [payment]
                    ON [payment].[OrderId] = [order].[Id]
                CROSS APPLY
                (
                    SELECT
                        CASE
                            WHEN COALESCE(
                                (SELECT MIN([history].[CreatedAt])
                                 FROM [OrderStatusHistories] AS [history]
                                 WHERE [history].[OrderId] = [order].[Id]
                                   AND [history].[ToStatus] = 6),
                                [shipment].[DeliveredAt]) < [shipment].[DeliveredAt]
                                THEN [shipment].[DeliveredAt]
                            ELSE COALESCE(
                                (SELECT MIN([history].[CreatedAt])
                                 FROM [OrderStatusHistories] AS [history]
                                 WHERE [history].[OrderId] = [order].[Id]
                                   AND [history].[ToStatus] = 6),
                                [shipment].[DeliveredAt])
                        END AS [ReceivedAt],
                        COALESCE(
                            (SELECT MIN([history].[OccurredAt])
                             FROM [PaymentStatusHistories] AS [history]
                             WHERE [history].[PaymentId] = [payment].[Id]
                               AND [history].[ToStatus] = 4),
                            (SELECT MIN([history].[CreatedAt])
                             FROM [OrderStatusHistories] AS [history]
                             WHERE [history].[OrderId] = [order].[Id]
                               AND [history].[ToStatus] = 6),
                            [shipment].[DeliveredAt]) AS [RefundedAt]
                ) AS [timeline]
                WHERE [order].[Status] = 6;

                INSERT INTO [OrderStatusHistories]
                    ([Id], [OrderId], [ChangedByUserId], [FromStatus],
                     [ToStatus], [Note], [CreatedAt])
                SELECT
                    NEWID(),
                    [order].[Id],
                    NULL,
                    6,
                    9,
                    N'Đồng bộ trạng thái hoàn tiền khi nâng cấp dữ liệu',
                    [returnRequest].[RefundedAt]
                FROM [Orders] AS [order]
                INNER JOIN [Payments] AS [payment]
                    ON [payment].[OrderId] = [order].[Id]
                   AND [payment].[Status] = 4
                INNER JOIN [ReturnRequests] AS [returnRequest]
                    ON [returnRequest].[OrderId] = [order].[Id]
                WHERE [order].[Status] = 6;

                UPDATE [order]
                SET [Status] = 9
                FROM [Orders] AS [order]
                INNER JOIN [Payments] AS [payment]
                    ON [payment].[OrderId] = [order].[Id]
                   AND [payment].[Status] = 4
                WHERE [order].[Status] = 6;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_ReceivedByUserId",
                table: "ReturnRequests",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_RequestedByUserId",
                table: "ReturnRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_ReviewedByUserId",
                table: "ReturnRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_Status_RequestedAt_Id",
                table: "ReturnRequests",
                columns: new[] { "Status", "RequestedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_ReturnRequests_OrderId",
                table: "ReturnRequests",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CreatedByUserId",
                table: "Shipments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_Shipments_Carrier_TrackingNumber",
                table: "Shipments",
                columns: new[] { "Carrier", "TrackingNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Shipments_OrderId",
                table: "Shipments",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Shipments])
                    OR EXISTS (SELECT 1 FROM [ReturnRequests])
                    OR EXISTS (SELECT 1 FROM [Orders] WHERE [Status] > 6)
                    OR EXISTS
                    (
                        SELECT 1
                        FROM [OrderStatusHistories]
                        WHERE [ToStatus] > 6 OR [FromStatus] > 6
                    )
                BEGIN
                    THROW 51040,
                        'Cannot roll back fulfillment workflow while shipment or return data exists. Restore a pre-migration backup instead.',
                        1;
                END
                """);

            migrationBuilder.DropTable(
                name: "ReturnRequests");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistories_Status_Valid",
                table: "OrderStatusHistories",
                sql: "[ToStatus] BETWEEN 0 AND 6 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders",
                sql: "[Status] BETWEEN 0 AND 6");
        }
    }
}

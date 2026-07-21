using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenCartAndOrderFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Orders] WHERE LEN([ShippingAddress]) > 500 OR LEN(COALESCE([Note], '')) > 500)
                    THROW 51010, 'Order shipping addresses or notes exceed the configured maximum length.', 1;

                IF EXISTS (SELECT 1 FROM [Orders] WHERE [Status] NOT BETWEEN 0 AND 4 OR [TotalAmount] <= 0)
                    THROW 51011, 'Orders contain an invalid status or non-positive total amount.', 1;

                IF EXISTS (SELECT 1 FROM [OrderDetails] WHERE [UnitPrice] <= 0)
                    THROW 51012, 'Order details contain a non-positive unit price.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [CartItems]
                    GROUP BY [CartId], [ProductId]
                    HAVING SUM(CONVERT(bigint, [Quantity])) > 2147483647
                )
                    THROW 51013, 'Duplicate cart item quantities exceed the supported integer range.', 1;
                """);

            migrationBuilder.Sql(
                """
                UPDATE [CartItems]
                SET [UnitPrice] = [Products].[Price]
                FROM [CartItems]
                INNER JOIN [Products] ON [CartItems].[ProductId] = [Products].[Id];

                ;WITH [RankedCartItems] AS
                (
                    SELECT
                        [Id],
                        SUM(CONVERT(bigint, [Quantity])) OVER
                            (PARTITION BY [CartId], [ProductId]) AS [TotalQuantity],
                        ROW_NUMBER() OVER
                            (PARTITION BY [CartId], [ProductId] ORDER BY [Id]) AS [RowNumber]
                    FROM [CartItems]
                )
                UPDATE [CartItems]
                SET [Quantity] = CONVERT(int, [RankedCartItems].[TotalQuantity])
                FROM [CartItems]
                INNER JOIN [RankedCartItems] ON [CartItems].[Id] = [RankedCartItems].[Id]
                WHERE [RankedCartItems].[RowNumber] = 1;

                ;WITH [RankedCartItems] AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER
                            (PARTITION BY [CartId], [ProductId] ORDER BY [Id]) AS [RowNumber]
                    FROM [CartItems]
                )
                DELETE FROM [CartItems]
                WHERE [Id] IN
                (
                    SELECT [Id]
                    FROM [RankedCartItems]
                    WHERE [RowNumber] > 1
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_CartItems_CartId",
                table: "CartItems");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Products",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingAddress",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders",
                sql: "[Status] BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_TotalAmount_Positive",
                table: "Orders",
                sql: "[TotalAmount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderDetails_UnitPrice_Positive",
                table: "OrderDetails",
                sql: "[UnitPrice] > 0");

            migrationBuilder.CreateIndex(
                name: "UX_CartItems_CartId_ProductId",
                table: "CartItems",
                columns: new[] { "CartId", "ProductId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CartItems_UnitPrice_Positive",
                table: "CartItems",
                sql: "[UnitPrice] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status_Valid",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_TotalAmount_Positive",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderDetails_UnitPrice_Positive",
                table: "OrderDetails");

            migrationBuilder.DropIndex(
                name: "UX_CartItems_CartId_ProductId",
                table: "CartItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CartItems_UnitPrice_Positive",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "ShippingAddress",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId",
                table: "CartItems",
                column: "CartId");
        }
    }
}

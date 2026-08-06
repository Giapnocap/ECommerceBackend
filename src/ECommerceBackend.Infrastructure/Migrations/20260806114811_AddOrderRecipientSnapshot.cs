using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRecipientSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientPhone",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [orders]
                SET [orders].[RecipientName] = COALESCE(
                        NULLIF(LTRIM(RTRIM([users].[FullName])), N''),
                        N'Khách hàng'),
                    [orders].[RecipientPhone] = NULLIF(
                        LTRIM(RTRIM([users].[Phone])),
                        N'')
                FROM [Orders] AS [orders]
                INNER JOIN [Users] AS [users]
                    ON [users].[Id] = [orders].[UserId]
                WHERE [orders].[RecipientName] IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "RecipientName",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Orders])
                BEGIN
                    THROW 51041,
                        'Cannot roll back order recipient snapshots while order data exists. Restore a pre-migration backup instead.',
                        1;
                END
                """);

            migrationBuilder.DropColumn(
                name: "RecipientName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RecipientPhone",
                table: "Orders");
        }
    }
}

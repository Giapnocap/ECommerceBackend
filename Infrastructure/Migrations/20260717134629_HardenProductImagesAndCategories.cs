using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenProductImagesAndCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Products] WHERE [Price] <= 0)
                    THROW 51000, 'Products with non-positive prices must be corrected before applying this migration.', 1;

                IF EXISTS (SELECT 1 FROM [Products] WHERE LEN([Name]) > 200 OR LEN([Description]) > 2000)
                    THROW 51001, 'Product text exceeds the configured maximum length.', 1;

                IF EXISTS (SELECT 1 FROM [Categories] WHERE LEN([Name]) > 100)
                    THROW 51002, 'Category names exceed the configured maximum length.', 1;

                IF EXISTS (SELECT 1 FROM [ProductImages] WHERE LEN([ImageUrl]) > 500)
                    THROW 51003, 'Product image URLs exceed the configured maximum length.', 1;

                IF EXISTS (SELECT 1 FROM [Categories] WHERE [ParentId] = [Id])
                    THROW 51004, 'Self-referencing categories must be corrected before applying this migration.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Products",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "ProductImages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Price_Positive",
                table: "Products",
                sql: "[Price] > 0");

            migrationBuilder.Sql(
                """
                ;WITH [RankedImages] AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY [ProductId]
                            ORDER BY CASE WHEN [IsMain] = 1 THEN 0 ELSE 1 END, [Id]
                        ) AS [RowNumber]
                    FROM [ProductImages]
                )
                UPDATE [ProductImages]
                SET [IsMain] = CASE WHEN [RankedImages].[RowNumber] = 1 THEN 1 ELSE 0 END
                FROM [ProductImages]
                INNER JOIN [RankedImages] ON [ProductImages].[Id] = [RankedImages].[Id];
                """);

            migrationBuilder.CreateIndex(
                name: "UX_ProductImages_ProductId_IsMain",
                table: "ProductImages",
                column: "ProductId",
                unique: true,
                filter: "[IsMain] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Categories_Parent_NotSelf",
                table: "Categories",
                sql: "[ParentId] IS NULL OR [ParentId] <> [Id]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Price_Positive",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "UX_ProductImages_ProductId_IsMain",
                table: "ProductImages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Categories_Parent_NotSelf",
                table: "Categories");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Products",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "ProductImages",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products",
                sql: "[Price] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages",
                column: "ProductId");
        }
    }
}

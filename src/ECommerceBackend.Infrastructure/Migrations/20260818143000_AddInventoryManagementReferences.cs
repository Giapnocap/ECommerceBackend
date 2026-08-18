using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryManagementReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "InventoryTransactions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions");
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions");
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions",
                sql: "[Type] BETWEEN 0 AND 5");
            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] IN (0, 5) AND [QuantityChange] > 0) OR ([Type] = 1 AND [QuantityChange] <> 0) OR ([Type] = 2 AND [QuantityChange] < 0) OR ([Type] IN (3, 4) AND [QuantityChange] > 0)");
            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] IN (0, 1, 5) AND [OrderId] IS NULL) OR ([Type] IN (2, 3, 4) AND [OrderId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [InventoryTransactions] SET [Type] = 1 WHERE [Type] = 5;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions");
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions");
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_Type_Valid",
                table: "InventoryTransactions",
                sql: "[Type] BETWEEN 0 AND 4");
            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_QuantityChange_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] = 0 AND [QuantityChange] > 0) OR ([Type] = 1 AND [QuantityChange] <> 0) OR ([Type] = 2 AND [QuantityChange] < 0) OR ([Type] IN (3, 4) AND [QuantityChange] > 0)");
            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryTransactions_OrderLink_MatchesType",
                table: "InventoryTransactions",
                sql: "([Type] IN (0, 1) AND [OrderId] IS NULL) OR ([Type] IN (2, 3, 4) AND [OrderId] IS NOT NULL)");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "InventoryTransactions");
        }
    }
}

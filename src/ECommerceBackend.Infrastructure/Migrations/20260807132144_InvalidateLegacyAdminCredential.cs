using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerceBackend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvalidateLegacyAdminCredential : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [PasswordHash] = '!BOOTSTRAP_REQUIRED!',
                    [TokenVersion] = [TokenVersion] + 1
                WHERE [Id] = '99999999-9999-9999-9999-999999999999'
                  AND HASHBYTES('SHA2_256', CONVERT(varchar(200), [PasswordHash]))
                      = 0x2C9E5FBFC4F3D30630E891ADDA3E081344A71DF5A424506DF6327037B087110B;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Invalidated credentials must never be restored by rollback.
        }
    }
}

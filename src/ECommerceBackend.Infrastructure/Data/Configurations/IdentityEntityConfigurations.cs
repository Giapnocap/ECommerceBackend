using ECommerceBackend.Application.Common;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.Property(user => user.RowVersion).IsRowVersion();
            builder.HasIndex(user => user.NormalizedUserName).IsUnique();
            builder.HasIndex(user => user.NormalizedEmail).IsUnique();
            builder.Property(user => user.UserName).HasMaxLength(50);
            builder.Property(user => user.NormalizedUserName).HasMaxLength(50);
            builder.Property(user => user.Email).HasMaxLength(254);
            builder.Property(user => user.NormalizedEmail).HasMaxLength(254);
            builder.Property(user => user.PasswordHash).HasMaxLength(200);
            builder.Property(user => user.FullName).HasMaxLength(100);
            builder.Property(user => user.Phone).HasMaxLength(20);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Users_FailedLoginCount_NonNegative",
                    "[FailedLoginCount] >= 0");
            });
        }
    }

    internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.HasIndex(role => role.Name).IsUnique();
            builder.HasData(AuthorizationSeedData.CreateRoles());
        }
    }

    internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
    {
        public void Configure(EntityTypeBuilder<UserRole> builder)
        {
            builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId });
            builder.HasIndex(userRole => userRole.UserId).IsUnique();

            builder.HasOne(userRole => userRole.User)
                .WithMany(user => user.UserRoles)
                .HasForeignKey(userRole => userRole.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(userRole => userRole.Role)
                .WithMany(role => role.UserRoles)
                .HasForeignKey(userRole => userRole.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
    {
        public void Configure(EntityTypeBuilder<Permission> builder)
        {
            builder.HasIndex(permission => permission.Name).IsUnique();
            builder.HasData(AuthorizationSeedData.CreatePermissions());
        }
    }

    internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
    {
        public void Configure(EntityTypeBuilder<RolePermission> builder)
        {
            builder.HasKey(rolePermission => new
            {
                rolePermission.RoleId,
                rolePermission.PermissionId
            });

            builder.HasOne(rolePermission => rolePermission.Role)
                .WithMany(role => role.RolePermissions)
                .HasForeignKey(rolePermission => rolePermission.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(rolePermission => rolePermission.Permission)
                .WithMany(permission => permission.RolePermissions)
                .HasForeignKey(rolePermission => rolePermission.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasData(AuthorizationSeedData.CreateAdminPermissions());
            builder.HasData(AuthorizationSeedData.CreateStaffPermissions());
        }
    }

    internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.Property(token => token.RowVersion).IsRowVersion();
            builder.HasIndex(token => token.TokenHash).IsUnique();
            builder.HasIndex(token => token.ExpiresAt)
                .HasDatabaseName("IX_RefreshTokens_ExpiresAt");
            builder.HasIndex(token => new { token.UserId, token.ExpiresAt });
            builder.HasIndex(token => new { token.UserId, token.FamilyId, token.ExpiresAt });
            builder.Property(token => token.TokenHash).HasMaxLength(128);
            builder.Property(token => token.ReplacedByTokenHash).HasMaxLength(128);
            builder.Property(token => token.RevocationReason).HasMaxLength(100);

            builder.HasOne(token => token.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
    {
        public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
        {
            builder.Property(token => token.RowVersion).IsRowVersion();
            builder.HasIndex(token => token.TokenHash).IsUnique();
            builder.HasIndex(token => token.ExpiresAt);
            builder.HasIndex(token => token.UserId)
                .HasDatabaseName("UX_PasswordResetTokens_UserId_Active")
                .HasFilter("[ConsumedAt] IS NULL AND [RevokedAt] IS NULL")
                .IsUnique();
            builder.Property(token => token.TokenHash).HasMaxLength(64);

            builder.HasOne(token => token.User)
                .WithMany(user => user.PasswordResetTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    internal sealed class EmailVerificationTokenConfiguration
        : IEntityTypeConfiguration<EmailVerificationToken>
    {
        public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
        {
            builder.Property(token => token.RowVersion).IsRowVersion();
            builder.HasIndex(token => token.TokenHash).IsUnique();
            builder.HasIndex(token => token.ExpiresAt);
            builder.HasIndex(token => token.UserId)
                .HasDatabaseName("UX_EmailVerificationTokens_UserId_Active")
                .HasFilter("[ConsumedAt] IS NULL AND [RevokedAt] IS NULL")
                .IsUnique();
            builder.Property(token => token.TokenHash).HasMaxLength(64);

            builder.HasOne(token => token.User)
                .WithMany(user => user.EmailVerificationTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    internal static class AuthorizationSeedData
    {
        private static readonly Guid AdminRoleId =
            Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid StaffRoleId =
            Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid CustomerRoleId =
            Guid.Parse("33333333-3333-3333-3333-333333333333");

        public static Role[] CreateRoles() =>
        [
            new Role { Id = AdminRoleId, Name = RoleNames.Admin },
            new Role { Id = StaffRoleId, Name = RoleNames.Staff },
            new Role { Id = CustomerRoleId, Name = RoleNames.Customer }
        ];

        public static Permission[] CreatePermissions() =>
            PermissionNames.All.Select((name, index) => new Permission
            {
                Id = Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa{index + 1:D3}"),
                Name = name
            }).ToArray();

        public static RolePermission[] CreateAdminPermissions() =>
            CreatePermissions().Select(permission => new RolePermission
            {
                RoleId = AdminRoleId,
                PermissionId = permission.Id
            }).ToArray();

        public static RolePermission[] CreateStaffPermissions()
        {
            var permissions = CreatePermissions();
            return PermissionNames.StaffPermissions
                .Select(permissionName => new RolePermission
                {
                    RoleId = StaffRoleId,
                    PermissionId = permissions
                        .Single(permission => permission.Name == permissionName)
                        .Id
                })
                .ToArray();
        }
    }
}

using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Tests;

public sealed class UserInvariantTests
{
    [Fact]
    public void UpdateProfile_NormalizesFieldsAndRejectsInvalidPhoneAtomically()
    {
        var user = CreateUser();
        user.UpdateProfile("  Customer Name  ", " 0901234567 ");

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            user.UpdateProfile("Changed Name", "invalid"));

        Assert.Equal("user_phone_invalid", exception.Code);
        Assert.Equal("Customer Name", user.FullName);
        Assert.Equal("0901234567", user.Phone);
    }

    [Fact]
    public void ChangeRole_ReplacesAssignmentsAndInvalidatesSessionsOnce()
    {
        var user = CreateUser();
        var customer = Role("Customer");
        var staff = Role("Staff");
        user.UserRoles.Add(UserRole.Create(user.Id, customer));

        var changed = user.ChangeRole(staff);
        var unchanged = user.ChangeRole(staff);

        Assert.True(changed.Changed);
        Assert.False(unchanged.Changed);
        Assert.Equal(1, user.TokenVersion);
        Assert.Equal(staff.Id, Assert.Single(user.UserRoles).RoleId);
        Assert.Single(changed.PreviousAssignments);
    }

    [Fact]
    public void MarkDeleted_IsIdempotentAndBlocksFutureRoleChanges()
    {
        var user = CreateUser();
        var customer = Role("Customer");
        user.UserRoles.Add(UserRole.Create(user.Id, customer));

        Assert.True(user.MarkDeleted());
        Assert.False(user.MarkDeleted());
        Assert.Equal(1, user.TokenVersion);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            user.ChangeRole(Role("Staff")));
        Assert.Equal("user_deleted", exception.Code);
        Assert.Equal(customer.Id, Assert.Single(user.UserRoles).RoleId);
    }

    [Fact]
    public void AdministratorLock_RequiresExplicitUnlockAndInvalidatesSessions()
    {
        var user = CreateUser();
        var now = new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(user.LockByAdministrator());
        Assert.True(user.IsLockedOutAt(now.AddYears(10)));
        Assert.Equal(DateTime.MaxValue, user.LockoutEndAt);
        Assert.Equal(1, user.TokenVersion);
        Assert.False(user.LockByAdministrator());

        Assert.True(user.UnlockByAdministrator());
        Assert.False(user.IsLockedOutAt(now));
        Assert.Null(user.LockoutEndAt);
        Assert.Equal(2, user.TokenVersion);
    }

    [Fact]
    public void VerifyEmail_IsOneWayAndRejectsTimeBeforeRegistration()
    {
        var user = CreateUser();
        user.CreatedAt = new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            user.VerifyEmail(user.CreatedAt.AddSeconds(-1)));
        Assert.Equal("user_email_verification_time_invalid", exception.Code);
        Assert.Null(user.EmailVerifiedAt);

        var verifiedAt = user.CreatedAt.AddMinutes(1);
        Assert.True(user.VerifyEmail(verifiedAt));
        Assert.False(user.VerifyEmail(verifiedAt.AddMinutes(1)));
        Assert.Equal(verifiedAt, user.EmailVerifiedAt);
    }

    [Fact]
    public void IdentityInvariantSetters_AreNotPublic()
    {
        Assert.False(
            typeof(User).GetProperty(nameof(User.IsDeleted))!
                .SetMethod!
                .IsPublic);
        Assert.False(
            typeof(UserRole).GetProperty(nameof(UserRole.UserId))!
                .SetMethod!
                .IsPublic);
        Assert.False(
            typeof(UserRole).GetProperty(nameof(UserRole.RoleId))!
                .SetMethod!
                .IsPublic);
    }

    private static User CreateUser()
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = "customer",
            Email = "customer@example.com",
            FullName = "Customer",
            PasswordHash = "hash"
        };

    private static Role Role(string name)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name
        };
}

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

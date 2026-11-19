using SzuBadminton.Services;
using Xunit;

namespace SzuBadminton.Tests;

public sealed class AccountSessionRulesTests
{
    [Theory]
    [InlineData(true, "first-account", "second-account", true)]
    [InlineData(true, "second-account", "second-account", false)]
    [InlineData(true, null, "second-account", true)]
    [InlineData(false, "first-account", "second-account", false)]
    [InlineData(true, "first-account", "", false)]
    public void SessionReset_OnlyRunsForDifferentSelectedAccountAtStartup(
        bool autoLogin,
        string? authenticatedAccountId,
        string? selectedAccountId,
        bool expected)
    {
        Assert.Equal(
            expected,
            AccountSessionRules.RequiresSessionReset(autoLogin, authenticatedAccountId, selectedAccountId));
    }
}

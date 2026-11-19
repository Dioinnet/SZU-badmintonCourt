namespace SzuBadminton.Services;

public static class AccountSessionRules
{
    public static bool RequiresSessionReset(bool autoLogin, string? authenticatedAccountId, string? selectedAccountId) =>
        autoLogin
        && !string.IsNullOrWhiteSpace(selectedAccountId)
        && !string.Equals(authenticatedAccountId, selectedAccountId, StringComparison.Ordinal);
}

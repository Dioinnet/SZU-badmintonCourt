using System.Security.Cryptography;
using System.Text;

namespace SzuBadminton.Services;

public static class CredentialProtector
{
    public static string Protect(string plainText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);
        var clearBytes = Encoding.UTF8.GetBytes(plainText);
        try
        {
            var encrypted = ProtectedData.Protect(clearBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public static string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            throw new InvalidOperationException("该账号尚未保存密码。");
        }

        var clearBytes = Array.Empty<byte>();
        try
        {
            clearBytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("密码无法由当前 Windows 用户解密，请重新保存该账号。", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }
}

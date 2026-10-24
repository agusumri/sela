using System.Security.Cryptography;
using System.Text;

namespace Sela.Services;

/// <summary>
/// DPAPI (CurrentUser) protection for secrets persisted in settings.json, e.g. the Google iCal URL.
/// Protected values are stored as "dpapi:" + base64; anything else is treated as legacy plaintext.
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Sela.SecretProtector.v1");

    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(data);
    }

    /// <summary>Returns the plaintext; legacy plaintext is returned as-is; undecryptable values (other user/machine) yield null.</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        if (!IsProtected(stored)) return stored;
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            Log.Write("Gagal mendekripsi URL Google Calendar; diabaikan.");
            return null;
        }
    }
}

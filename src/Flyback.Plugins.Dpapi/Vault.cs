using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Flyback.Plugins.Dpapi;

/// <summary>
/// The part that actually talks to Windows. Kept in its own file so that
/// loading the plugin does not load the data-protection package with it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Vault
{
    /// <summary>
    /// Mixed into the protection so a blob is bound to this application as well
    /// as to the account. Not a secret and not doing a secret's job — it stops
    /// one program's stored value being handed to another that happens to guess
    /// the path.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Flyback.Assist.v1");

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Flyback",
        "secrets");

    public static void Keep(string account, string secret)
    {
        Directory.CreateDirectory(Folder);

        var sealed_ = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret),
            Entropy,
            DataProtectionScope.CurrentUser);

        File.WriteAllBytes(PathFor(account), sealed_);
    }

    public static string? Recall(string account)
    {
        var file = PathFor(account);

        if (!File.Exists(file)) return null;

        try
        {
            var opened = ProtectedData.Unprotect(
                File.ReadAllBytes(file),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(opened);
        }
        catch (CryptographicException)
        {
            // Written by another account, or on another machine, or corrupted.
            // Not something anybody can act on, and not a reason to fail: the
            // caller treats it as no key and asks for one.
            return null;
        }
    }

    public static void Forget(string account)
    {
        var file = PathFor(account);

        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>
    /// One file per account. The name is scrubbed rather than trusted — these
    /// come from a plugin's own id today, but a path is not the place to find
    /// out that will always be true.
    /// </summary>
    private static string PathFor(string account)
    {
        var safe = new string([.. account.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_')]);

        if (safe.Length == 0) safe = "unnamed";

        return Path.Combine(Folder, safe + ".bin");
    }
}

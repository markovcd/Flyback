namespace Flyback.Assist.Keys;

/// <summary>Where a host may keep a key: the operating system's own store, if this one has one.</summary>
internal sealed class SecretStores(IReadOnlyList<ISecretStore> all)
{
    /// <summary>Windows' data protection, the macOS Keychain and the Linux Secret Service; each answers only on its own system.</summary>
    public static SecretStores Shipped { get; } = new([new DpapiSecretStore(), new KeychainSecretStore(), new KeyringSecretStore()]);

    public static SecretStores None { get; } = new([]);

    public IReadOnlyList<ISecretStore> All { get; } = all;

    /// <summary>
    /// Where a secret should be kept here: supported, highest priority, ties broken on id.
    /// Null when nothing can hold one, in which case a key is used for the session and then
    /// forgotten, which the panel says rather than appearing to have saved something.
    /// </summary>
    public ISecretStore? Preferred => All
        .Where(Supported)
        .OrderByDescending(s => s.Priority)
        .ThenBy(s => s.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    private static bool Supported(ISecretStore store)
    {
        try
        {
            return store.IsSupported;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }
}

using Flyback.Plugins.Secrets;

namespace Flyback.App.Assist;

/// <summary>Where a key came from. Shown to the person, because the three differ in what they promise.</summary>
public enum CredentialSource
{
    /// <summary>There is no key for this provider.</summary>
    None,

    /// <summary>From the environment. Flyback never wrote it and never will.</summary>
    Environment,

    /// <summary>From the operating system's own store, put there at somebody's request.</summary>
    Kept,

    /// <summary>Typed in and held for this run only. Gone when the window closes.</summary>
    Session,
}

/// <summary>
/// Where an assistant's key comes from, in order of preference.
/// </summary>
/// <remarks>
/// <para>
/// The environment wins and is never written back. That is free, and it is the
/// answer for anyone who would rather Flyback stayed nowhere near their
/// credentials — and for a machine with no keyring at all.
/// </para>
/// <para>
/// Failing that, the operating system's store, if a plugin offered one. Failing
/// that, memory for this run only: honest, and better than a file we would have
/// to pretend was safe. Nothing here ever writes a secret to disk itself; see
/// ADR-0034.
/// </para>
/// </remarks>
public sealed class Credentials(ISecretStore? store)
{
    private readonly Dictionary<string, string> session = new(StringComparer.Ordinal);

    /// <summary>Whether a key can outlive the window, and what would hold it if so.</summary>
    public ISecretStore? Store { get; } = store;

    public bool CanKeep => Store is not null;

    /// <summary>The key to use, or null when there is none.</summary>
    public string? Of(string account, string environmentVariable) =>
        FromEnvironment(environmentVariable) ?? FromStore(account) ?? session.GetValueOrDefault(account);

    /// <summary>Where <see cref="Of"/> would get it, so the panel can say so.</summary>
    public CredentialSource SourceOf(string account, string environmentVariable)
    {
        if (FromEnvironment(environmentVariable) is not null) return CredentialSource.Environment;
        if (FromStore(account) is not null) return CredentialSource.Kept;

        return session.ContainsKey(account) ? CredentialSource.Session : CredentialSource.None;
    }

    /// <summary>
    /// Takes a key someone typed. <paramref name="keep"/> asks for it to outlive
    /// the window, which only happens if something installed can hold it — a
    /// caller that assumed otherwise would be lying to the person on its behalf,
    /// so ask <see cref="CanKeep"/> first and say which it will be.
    /// </summary>
    public void Accept(string account, string secret, bool keep)
    {
        session[account] = secret;

        if (!keep || Store is null) return;

        try
        {
            Store.Keep(account, secret);
        }
        catch
        {
            // The store refused. The key still works for this run, and the panel
            // reads the source back rather than trusting that this worked.
        }
    }

    /// <summary>Removes a key from everywhere this can reach.</summary>
    public void Forget(string account)
    {
        session.Remove(account);

        try
        {
            Store?.Forget(account);
        }
        catch
        {
            // Nothing useful to do, and nothing worth taking the window down for.
        }
    }

    private static string? FromEnvironment(string variable) =>
        string.IsNullOrWhiteSpace(variable)
            ? null
            : Blank(Environment.GetEnvironmentVariable(variable));

    private string? FromStore(string account)
    {
        try
        {
            return Blank(Store?.Recall(account));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>An empty variable is the same as an unset one, and catches the classic export of nothing.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

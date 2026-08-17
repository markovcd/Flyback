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
/// A key somebody entered wins, because entering one is a deliberate act and an
/// exported variable is the room somebody is standing in. The other way round —
/// which this did until it was tried — means typing a key into the settings on a
/// machine that exports one has no effect whatever, and cannot be made to have
/// one from inside the application at all.
/// </para>
/// <para>
/// This session first, then the store: <see cref="Accept"/> writes both when it
/// is asked to keep, so they agree whenever they can, and where they cannot it
/// is because somebody just typed a key and declined to keep it. That one is the
/// newer, and answering with the old one would be the same bug in miniature.
/// </para>
/// <para>
/// Then the environment, which is never written back — someone who exports a key
/// has said where it lives, and taking a copy would be deciding otherwise on
/// their behalf. That was always the rule that mattered, and losing the top spot
/// costs it nothing. <see cref="Forget"/> is the way back to it, which is what
/// makes an entered key safe to prefer.
/// </para>
/// <para>
/// Nothing here ever writes a secret to disk itself; see ADR-0034.
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
        session.GetValueOrDefault(account) ?? FromStore(account) ?? FromEnvironment(environmentVariable);

    /// <summary>
    /// Whether a key somebody entered here exists at all. Not the same question
    /// as <see cref="SourceOf"/>, which names the one in force: this is what
    /// <see cref="Forget"/> would have to work on, and what tells a panel that
    /// an entered key is sitting on top of an environment variable it could fall
    /// back to.
    /// </summary>
    public bool HasEntered(string account) =>
        FromStore(account) is not null || session.ContainsKey(account);

    /// <summary>Where <see cref="Of"/> would get it, so the panel can say so.</summary>
    public CredentialSource SourceOf(string account, string environmentVariable)
    {
        // The same order as Of, and it has to stay the same order: this is the
        // sentence the panel prints, and one that disagreed with what was
        // actually sent would be worse than no sentence at all.
        var entered = session.GetValueOrDefault(account);
        var kept = FromStore(account);

        if (entered is not null)
        {
            // Kept rather than Session when the store has the same secret, even
            // though the session is holding it too. Accept keeps a copy in
            // memory whatever happens, so that it survives a store that failed —
            // but reporting "gone when this window closes" about a key that is
            // on disk is the lie ADR-0034 exists to prevent, and in the other
            // direction. Read back rather than assumed, for the same reason:
            // Keep not throwing is not the same as Keep having worked.
            return string.Equals(entered, kept, StringComparison.Ordinal)
                ? CredentialSource.Kept
                : CredentialSource.Session;
        }

        if (kept is not null) return CredentialSource.Kept;

        return FromEnvironment(environmentVariable) is not null
            ? CredentialSource.Environment
            : CredentialSource.None;
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

    /// <summary>
    /// Puts the key already in hand into the store, for somebody who typed one
    /// and then decided to keep it.
    /// </summary>
    /// <remarks>
    /// The field empties itself once a key has been taken, so without this the
    /// only way to change one's mind about keeping is to type the whole secret
    /// again — a secret this is already holding, and which the person may well
    /// have pasted from somewhere they have since closed.
    /// </remarks>
    public void KeepWhatIsHeld(string account)
    {
        if (Store is null || session.GetValueOrDefault(account) is not { } secret) return;

        try
        {
            Store.Keep(account, secret);
        }
        catch
        {
            // As Accept: the key still works for this run, and the panel reads
            // the source back rather than trusting that this worked.
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

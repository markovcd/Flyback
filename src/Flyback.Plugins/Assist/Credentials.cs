using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// Where an assistant's key comes from, in order of preference, and the transport that
/// sends it (<see cref="Transport"/>): the key itself never reaches the assistant.
/// </summary>
/// <remarks>
/// A key somebody entered wins, because entering one is a deliberate act and an
/// exported variable is the room they are standing in — the other way round, typing
/// a key on a machine that exports one could have no effect at all.
/// <para>
/// This session first, then the store: <see cref="Accept"/> writes both when asked
/// to keep, and where they disagree it is because somebody just typed a key and
/// declined to keep it. Then the environment, which is never written back —
/// <see cref="Forget"/> is the way back to it. Nothing here writes a secret to disk
/// itself (ADR-0034).
/// </para>
/// <para>
/// An entered key is bound to the origin the assistant sent to when it was entered, and
/// kept with it. A variable is bound to wherever the assistant sends now, being the room
/// somebody is standing in rather than a thing they handed over.
/// </para>
/// </remarks>
internal sealed class Credentials(ISecretStore? store)
{
    private readonly Dictionary<string, BoundKey> session = new(StringComparer.Ordinal);

    /// <summary>Whether a key can outlive the window, and what would hold it if so.</summary>
    public ISecretStore? Store { get; } = store;

    public bool CanKeep => Store is not null;

    /// <summary>
    /// The key to use, or null when there is none. <paramref name="origin"/> is where the
    /// assistant sends now, which a variable, or a key kept before keys had an origin, is bound to.
    /// </summary>
    public BoundKey? Of(string account, string environmentVariable, string? origin) =>
        session.GetValueOrDefault(account) ?? FromStore(account, origin) ?? FromEnvironment(environmentVariable, origin);

    /// <summary>What <paramref name="assistant"/> sends over, configured as <paramref name="values"/>.</summary>
    /// <param name="network">Where signed requests go; the network unless a test says otherwise.</param>
    public IAssistantTransport Transport(IPatchAssistant assistant, SettingValues values, HttpMessageHandler? network = null)
    {
        var key = Of(assistant.Id, assistant.Credential.EnvironmentVariable, KeyedTransport.OriginOf(assistant, values));

        return new KeyedTransport(key?.Secret, key?.Origin, assistant.Credential, network);
    }

    /// <summary>
    /// Why the key in <paramref name="config"/> will not be sent where <paramref name="assistant"/>
    /// is set to send, or null where it will.
    /// </summary>
    public static string? Elsewhere(IPatchAssistant assistant, AssistantConfig config)
    {
        var now = KeyedTransport.OriginOf(assistant, config.Values);

        if (!config.Transport.HasKey)
        {
            var variable = assistant.Credential.EnvironmentVariable;

            if (string.IsNullOrWhiteSpace(variable) || Blank(Environment.GetEnvironmentVariable(variable)) is not { } exported)
                return null;

            if (KeySafety.Refused(exported) is { } why) return $"{variable} is not sent: {why}";

            return KeySafety.Cleartext(now)
                ? $"{variable} is not sent to {now}: over plain http it would cross the network readable. "
                  + "A server that takes any value as a key can be given one here."
                : null;
        }

        if (config.Transport.Origin is not { } bound) return null;

        return now is null || string.Equals(now, bound, StringComparison.Ordinal)
            ? null
            : $"The key was entered for {bound}, and this is set to send to {now}. Enter it again to send it there.";
    }

    /// <summary>
    /// Whether a key somebody entered here exists at all. Not the same question
    /// as <see cref="SourceOf"/>, which names the one in force: this is what
    /// <see cref="Forget"/> would have to work on, and what tells a panel that
    /// an entered key is sitting on top of an environment variable it could fall
    /// back to.
    /// </summary>
    public bool HasEntered(string account) =>
        FromStore(account, null) is not null || session.ContainsKey(account);

    /// <summary>Where <see cref="Of"/> would get it, so the panel can say so.</summary>
    public CredentialSource SourceOf(string account, string environmentVariable)
    {
        // The same order as Of, and it has to stay the same order: this is the
        // sentence the panel prints, and one that disagreed with what was
        // actually sent would be worse than no sentence at all.
        var entered = session.GetValueOrDefault(account);
        var kept = FromStore(account, entered?.Origin);

        if (entered is not null)
        {
            // Kept rather than Session when the store has the same key, even
            // though the session is holding it too. Accept keeps a copy in
            // memory whatever happens, so that it survives a store that failed —
            // but reporting "gone when this window closes" about a key that is
            // on disk is the lie ADR-0034 exists to prevent, and in the other
            // direction. Read back rather than assumed, for the same reason:
            // Keep not throwing is not the same as Keep having worked.
            return entered == kept ? CredentialSource.Kept : CredentialSource.Session;
        }

        if (kept is not null) return CredentialSource.Kept;

        return FromEnvironment(environmentVariable, "") is not null
            ? CredentialSource.Environment
            : CredentialSource.None;
    }

    /// <summary>
    /// Takes a key someone typed, bound to <paramref name="origin"/>. <paramref name="keep"/>
    /// asks for it to outlive the window, which only happens if something installed can hold
    /// it — a caller that assumed otherwise would be lying to the person on its behalf, so ask
    /// <see cref="CanKeep"/> first and say which it will be.
    /// </summary>
    public void Accept(string account, string secret, string origin, bool keep)
    {
        var key = new BoundKey(secret, origin);

        session[account] = key;

        if (keep) Keep(account, key);
    }

    /// <summary>
    /// Puts the key already in hand into the store, for somebody who typed one and
    /// then decided to keep it. The field empties itself once a key has been taken,
    /// so without this the only way to change one's mind is to type the whole secret
    /// again.
    /// </summary>
    public void KeepWhatIsHeld(string account)
    {
        if (session.GetValueOrDefault(account) is { } key) Keep(account, key);
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

    private void Keep(string account, BoundKey key)
    {
        if (Store is null) return;

        try
        {
            Store.Keep(account, key.Stored());
        }
        catch
        {
            // The store refused. The key still works for this run, and the panel
            // reads the source back rather than trusting that this worked.
        }
    }

    /// <summary>
    /// A variable bound to where the assistant sends now, except over plain http to another
    /// machine: an exported key is a real one, and nobody typed it for that address.
    /// </summary>
    private static BoundKey? FromEnvironment(string variable, string? origin) =>
        string.IsNullOrWhiteSpace(variable) || origin is null || KeySafety.Cleartext(origin)
        || Blank(Environment.GetEnvironmentVariable(variable)) is not { } secret
            ? null
            : new BoundKey(secret, origin);

    /// <summary>What the store holds; a key kept alone is bound to <paramref name="origin"/> and kept again with it.</summary>
    private BoundKey? FromStore(string account, string? origin)
    {
        string? stored;

        try
        {
            stored = Blank(Store?.Recall(account));
        }
        catch
        {
            return null;
        }

        var key = BoundKey.Read(stored, origin ?? "");

        if (key is not null && origin is not null && key.Stored() != stored) Keep(account, key);

        return key;
    }

    /// <summary>An empty variable is the same as an unset one, and catches the classic export of nothing.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

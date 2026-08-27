using System.Diagnostics;
using System.Text;

namespace Flyback.Plugins.Keychain;

/// <summary>
/// The part that actually talks to macOS. Kept in its own file so that loading
/// the plugin does not go anywhere near a process, which is the rule the
/// Windows store's <c>Vault</c> follows for its package.
/// </summary>
/// <remarks>
/// <para>
/// <c>/usr/bin/security</c> rather than the Security framework, which is the
/// call ADR-0034 made when it wrote this plugin down as forty lines of shelling
/// out. The framework would mean CoreFoundation dictionaries, constants read out
/// of the framework by symbol, and a lifetime rule per object — a great deal of
/// unfamiliar interop for three operations, in the one place where a bug is a
/// disclosure rather than a glitch. The tool is part of the operating system and
/// is what every other program in this position drives.
/// </para>
/// <para>
/// It settles the keychain's access control by itself, too. The item is created
/// by <c>security</c> and read back by <c>security</c>, so the keychain sees one
/// program both times and never raises the "wants to access" panel that a
/// re-signed application would. The cost is that any program run by this user
/// can ask <c>security</c> for the same item — which is the threat model the
/// Windows store already has, where anything running as the account can undo
/// what the account protected.
/// </para>
/// </remarks>
internal static class LoginKeychain
{
    private const string Security = "/usr/bin/security";

    /// <summary>
    /// The service every item is filed under, so that the accounts inside it are
    /// the provider ids and nothing else of ours is mixed in with them.
    /// </summary>
    private const string Service = "Flyback";

    /// <summary>
    /// What <c>security</c> exits with when the item is not in the keychain. It
    /// hands back the <c>OSStatus</c> it was given and an exit code is one byte,
    /// so <c>errSecItemNotFound</c> — -25300 — arrives as its low eight bits.
    /// An absence rather than a failure, and the two have to be told apart: one
    /// is a person who has not entered a key yet, and the other is worth
    /// throwing about.
    /// </summary>
    private const int NotFound = 44;

    /// <summary>
    /// Long enough that a keychain asking somebody to unlock it is not mistaken
    /// for a keychain that has stopped answering. It is not a promise about how
    /// fast macOS is — it is the difference between a slow answer and a window
    /// that never comes back, because these calls are made on the UI thread.
    /// </summary>
    private const int PatienceMilliseconds = 30_000;

    /// <summary>
    /// Replaces whatever is there — <c>-U</c> updates an existing item, without
    /// which a second key for the same provider fails as a duplicate.
    /// </summary>
    /// <remarks>
    /// The secret is an argument, which is the one thing about this that is not
    /// ideal: for as long as the call takes, another program run by this same
    /// user could read it out of the process list. macOS does not show one
    /// user's arguments to another, and a program running as this user can ask
    /// <c>security</c> for the key outright anyway — so it widens nothing that
    /// was not already open. The alternative is worse rather than better:
    /// <c>security</c> asked for a password it was not given reads it from the
    /// terminal, and would hang the window whenever Flyback was started from
    /// one.
    /// </remarks>
    public static void Keep(string account, string secret)
    {
        var outcome = Run(["add-generic-password", "-a", account, "-s", Service, "-U", "-w", secret]);

        if (outcome.Status != 0)
            throw new InvalidOperationException($"the keychain would not hold the key: {outcome.Complaint}.");
    }

    /// <summary>
    /// The key, or null when there is none — and also when the keychain is
    /// locked, or refused, or is not answering. None of those is something the
    /// person can act on differently: the panel asks for a key either way, which
    /// is how the Windows store treats a blob it cannot open.
    /// </summary>
    public static string? Recall(string account)
    {
        Outcome outcome;

        try
        {
            outcome = Run(["find-generic-password", "-a", account, "-s", Service, "-w"]);
        }
        catch
        {
            return null;
        }

        if (outcome.Status != 0) return null;

        // The tool prints the key and then a newline of its own. Exactly one is
        // taken back off, so a key is what it was — the only secret this cannot
        // round-trip is one that ends in a newline, which is not a thing any
        // provider issues.
        var key = outcome.Output;

        return key.EndsWith('\n') ? key[..^1] : key;
    }

    /// <summary>
    /// Removes the item. Nothing there to remove is not an error — the contract
    /// says so, and the panel offers Forget whether or not anything was kept.
    /// </summary>
    public static void Forget(string account)
    {
        var outcome = Run(["delete-generic-password", "-a", account, "-s", Service]);

        if (outcome.Status is 0 or NotFound) return;

        throw new InvalidOperationException($"the keychain would not let the key go: {outcome.Complaint}.");
    }

    /// <summary>
    /// Runs <c>security</c> once and waits for it. Arguments go across as a list
    /// rather than a line, so nothing here has to think about quoting a key that
    /// may contain anything at all.
    /// </summary>
    private static Outcome Run(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Security)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Named rather than inherited: the default comes from the console,
            // and an encoding with a byte-order mark would put one on the front
            // of a key that is read back.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var security = Process.Start(start)
            ?? throw new InvalidOperationException($"could not start {Security}.");

        // Both pipes are drained while the program runs. Reading one to the end
        // first would deadlock if the other filled up, which is unlikely with a
        // tool this terse and is not a thing to be lucky about.
        var output = security.StandardOutput.ReadToEndAsync();
        var errors = security.StandardError.ReadToEndAsync();

        if (!security.WaitForExit(PatienceMilliseconds))
        {
            security.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Security} did not answer within {PatienceMilliseconds / 1000} seconds.");
        }

        // The one that takes no timeout is also the one that waits for the pipes
        // above to close, which is what makes the two tasks finished below.
        security.WaitForExit();

        return new Outcome(security.ExitCode, output.Result, errors.Result);
    }

    private readonly record struct Outcome(int Status, string Output, string Errors)
    {
        /// <summary>What went wrong, in the tool's own words where it left any.</summary>
        public string Complaint =>
            Errors.Trim() is { Length: > 0 } said ? said : $"security exited with {Status}";
    }
}

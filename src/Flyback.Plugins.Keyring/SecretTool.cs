using System.Diagnostics;
using System.Text;

namespace Flyback.Plugins.Keyring;

/// <summary>
/// The part that actually talks to the keyring. Kept in its own file so that
/// loading the plugin does not go anywhere near a process, which is the rule the
/// Windows store's <c>Vault</c> follows for its package.
/// </summary>
/// <remarks>
/// <para>
/// <c>secret-tool</c> rather than libsecret, which is the call ADR-0034 made
/// when it wrote this plugin down as forty lines of shelling out. The library is
/// not the ALSA case: libsecret is GLib all the way down — a main loop, a type
/// system, a schema object and reference counting on every one of them, for
/// three operations. The tool is the front the library itself ships for exactly
/// this, and it is what carries the D-Bus conversation with GNOME Keyring or
/// KWallet on the other end.
/// </para>
/// <para>
/// The secret never becomes an argument: <c>store</c> reads it from standard
/// input, which is what that mode is for. Only the account name is on the
/// command line, and it is a provider id.
/// </para>
/// </remarks>
internal static class SecretTool
{
    private const string Program = "secret-tool";

    /// <summary>
    /// The attribute pair every item is filed under. Two rather than one so that
    /// a lookup is exact — the Secret Service matches on all the attributes it
    /// is given, and the keyring is shared with every other program on the
    /// machine.
    /// </summary>
    private const string ServiceAttribute = "service";

    private const string Service = "Flyback";

    private const string AccountAttribute = "account";

    /// <summary>
    /// Long enough that a keyring asking somebody to unlock it is not mistaken
    /// for a keyring that has stopped answering. It is not a promise about how
    /// fast D-Bus is — it is the difference between a slow answer and a window
    /// that never comes back, because these calls are made on the UI thread.
    /// </summary>
    private const int PatienceMilliseconds = 30_000;

    /// <summary>
    /// Whether this machine has somewhere to put a secret: the tool, and a
    /// session bus for it to talk over. Both are questions about the filesystem
    /// and the environment — nothing is started and no keyring is opened, which
    /// is what <see cref="Flyback.Plugins.Secrets.ISecretStore.IsSupported"/>
    /// asks for.
    /// </summary>
    /// <remarks>
    /// The bus is worth asking about separately. A headless server or a build
    /// container frequently has <c>secret-tool</c> installed as somebody else's
    /// dependency and no session for it to reach, and without this the answer
    /// would arrive as a key that appeared to have been saved and was not.
    /// </remarks>
    public static bool IsUsable => Executable is not null && HasSessionBus;

    /// <summary>
    /// Where <c>secret-tool</c> is, or null if it is nowhere on the path. Looked
    /// up each time rather than remembered, the way the ALSA backend asks
    /// whether libasound is installed: the answer is cheap, and a machine where
    /// it has just been installed should not have to be restarted to be believed.
    /// </summary>
    private static string? Executable
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrEmpty(path)) return null;

            foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                // A folder on PATH that cannot be combined with a file name is
                // somebody else's problem, and not a reason to stop looking
                // through the rest of them.
                try
                {
                    var candidate = Path.Combine(folder, Program);

                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Whether there is a session bus to reach the keyring over. The address is
    /// what a session sets; the socket under the runtime directory is where it
    /// is when nothing set the variable, which happens inside a desktop session
    /// started by systemd.
    /// </summary>
    private static bool HasSessionBus =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
        || (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
            && Path.Exists(Path.Combine(runtime, "bus")));

    /// <summary>
    /// Replaces whatever is there. Storing against attributes that already have
    /// an item is how the Secret Service is told to overwrite one, so there is
    /// nothing to remove first.
    /// </summary>
    public static void Keep(string account, string secret)
    {
        var outcome = Run(
            ["store", $"--label=Flyback ({account})", ServiceAttribute, Service, AccountAttribute, account],
            secret);

        if (outcome.Status != 0)
            throw new InvalidOperationException($"the keyring would not hold the key: {outcome.Complaint}.");
    }

    /// <summary>
    /// The key, or null when there is none — and also when the keyring is
    /// locked, or refused, or is not answering. None of those is something the
    /// person can act on differently: the panel asks for a key either way, which
    /// is how the Windows store treats a blob it cannot open.
    /// </summary>
    public static string? Recall(string account)
    {
        Outcome outcome;

        try
        {
            outcome = Run(["lookup", ServiceAttribute, Service, AccountAttribute, account]);
        }
        catch
        {
            return null;
        }

        // Nothing kept under that name exits non-zero with nothing on the
        // output, which is the same thing said twice and needs no telling apart.
        if (outcome.Status != 0) return null;

        // The tool prints the secret as it is, with no newline of its own — but
        // one is taken off if it is there, so that a version which adds one does
        // not hand back a key with a newline welded to the end. The only secret
        // this cannot round-trip is one that ends in a newline, which is not a
        // thing any provider issues.
        var key = outcome.Output;

        return key.EndsWith('\n') ? key[..^1] : key;
    }

    /// <summary>
    /// Removes the item. Nothing there to remove is not an error — the contract
    /// says so, <c>clear</c> agrees, and the panel offers Forget whether or not
    /// anything was ever kept.
    /// </summary>
    public static void Forget(string account)
    {
        var outcome = Run(["clear", ServiceAttribute, Service, AccountAttribute, account]);

        if (outcome.Status == 0) return;

        throw new InvalidOperationException($"the keyring would not let the key go: {outcome.Complaint}.");
    }

    /// <summary>
    /// Runs <c>secret-tool</c> once and waits for it. Arguments go across as a
    /// list rather than a line, so nothing here has to think about quoting.
    /// </summary>
    private static Outcome Run(IEnumerable<string> arguments, string? input = null)
    {
        var program = Executable
            ?? throw new InvalidOperationException($"{Program} is not installed on this machine.");

        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Named rather than inherited: the default comes from the console,
            // and an encoding with a byte-order mark would weld one to the front
            // of every key that went in or came back.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        // Only where there is something to write. Naming an encoding for a
        // stream that is not redirected is refused outright by Process.Start,
        // which would take the two calls that read nothing down with it.
        if (input is not null)
        {
            start.RedirectStandardInput = true;
            start.StandardInputEncoding = new UTF8Encoding(false);
        }

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var tool = Process.Start(start)
            ?? throw new InvalidOperationException($"could not start {program}.");

        if (input is not null)
        {
            // Written with no newline after it and then closed, which is what
            // tells the tool the secret has ended. A trailing newline would be
            // taken back off at the other end anyway; not writing one is the
            // version that does not depend on that.
            tool.StandardInput.Write(input);
            tool.StandardInput.Close();
        }

        // Both pipes are drained while the program runs. Reading one to the end
        // first would deadlock if the other filled up, which is unlikely with a
        // tool this terse and is not a thing to be lucky about.
        var output = tool.StandardOutput.ReadToEndAsync();
        var errors = tool.StandardError.ReadToEndAsync();

        if (!tool.WaitForExit(PatienceMilliseconds))
        {
            tool.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Program} did not answer within {PatienceMilliseconds / 1000} seconds.");
        }

        // The one that takes no timeout is also the one that waits for the pipes
        // above to close, which is what makes the two tasks finished below.
        tool.WaitForExit();

        return new Outcome(tool.ExitCode, output.Result, errors.Result);
    }

    private readonly record struct Outcome(int Status, string Output, string Errors)
    {
        /// <summary>What went wrong, in the tool's own words where it left any.</summary>
        public string Complaint =>
            Errors.Trim() is { Length: > 0 } said ? said : $"{Program} exited with {Status}";
    }
}

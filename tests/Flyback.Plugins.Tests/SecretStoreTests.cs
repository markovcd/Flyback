using Flyback.Core;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The stores that keep a key between runs. This is the one place where a bug
/// is a disclosure rather than a glitch, so they are tested against the real
/// thing rather than a stand-in — the round trip runs against whichever store
/// this machine actually has, and the other two are pinned in the direction
/// that is knowable from anywhere.
/// </summary>
public class SecretStoreTests
{
    /// <summary>Distinctive, so a stray blob is obviously the tests' and not somebody's key.</summary>
    private const string Account = "flyback-test-account";

    private const string Absent = "flyback-test-account-that-does-not-exist";

    /// <summary>The stores that ship in the box, one per operating system.</summary>
    public static TheoryData<string> PlatformStores => ["dpapi", "keychain", "secret-service"];

    private static PluginCatalog Shipped() => PluginHost.Load();

    private static ISecretStore Store(string id) => Shipped().SecretStores.Single(s => s.Id == id);

    /// <summary>
    /// The store this machine would actually use, or null where nothing
    /// installed can hold a key — which is a real answer on a Linux machine with
    /// no keyring, and the one the panel turns into "for this window only".
    /// </summary>
    private static ISecretStore? Here => Shipped().PreferredSecretStore;

    [Theory]
    [MemberData(nameof(PlatformStores))]
    public void A_store_in_a_plugin_reaches_the_catalogue(string id)
    {
        var catalog = Shipped();

        catalog.Problems.ShouldBeEmpty();
        catalog.SecretStores.Select(s => s.Id).ShouldContain(id);
    }

    /// <summary>
    /// All three plugins load everywhere; only the stores are tied to one
    /// system. Asserted in the direction that is knowable, the way the sound
    /// backends are: the Linux one also depends on whether the machine has a
    /// keyring at all, and a test that decided that for itself would be
    /// asserting its own copy of the implementation.
    /// </summary>
    [Fact]
    public void Support_is_answered_without_touching_a_keychain()
    {
        Store("dpapi").IsSupported.ShouldBe(OperatingSystem.IsWindows());
        Store("keychain").IsSupported.ShouldBe(OperatingSystem.IsMacOS());

        if (!OperatingSystem.IsLinux()) Store("secret-service").IsSupported.ShouldBeFalse();
    }

    [Fact]
    public void Being_asked_whether_it_works_here_never_throws()
    {
        foreach (var store in Shipped().SecretStores)
            Should.NotThrow(() => store.IsSupported);
    }

    /// <summary>
    /// The three stores claim the same priority, which is only safe because no
    /// machine supports two of them. If that ever stopped being true the choice
    /// would fall to the tie-break on id and Windows would quietly start
    /// preferring the Keychain, so pin the whole selection rather than the
    /// pieces.
    /// </summary>
    [Fact]
    public void The_store_chosen_here_is_the_one_for_this_operating_system()
    {
        var chosen = Here?.Id;

        // On Linux either answer is correct and which one is a property of the
        // machine: the keyring where secret-tool and a session bus are there,
        // and otherwise nothing — which is what makes the panel say a key is
        // held for this window rather than appearing to have saved it.
        if (OperatingSystem.IsLinux())
        {
            (chosen is null or "secret-service").ShouldBeTrue($"chose '{chosen}'");
            return;
        }

        chosen.ShouldBe(OperatingSystem.IsWindows() ? "dpapi" : OperatingSystem.IsMacOS() ? "keychain" : null);
    }

    /// <summary>
    /// The one that matters: a key put away comes back. Against whatever this
    /// machine has, so the same test covers data protection, the Keychain and
    /// the keyring, each on the only machine that could run it.
    /// </summary>
    [Fact]
    public void A_secret_survives_being_put_away_and_asked_for()
    {
        Assert.SkipWhen(Here is null, "nothing installed on this machine can hold a secret.");

        var store = Here;
        const string secret = "sk-a-value-that-is-not-a-real-key";

        try
        {
            store.Keep(Account, secret);

            store.Recall(Account).ShouldBe(secret);
        }
        finally
        {
            store.Forget(Account);
        }
    }

    [Fact]
    public void A_secret_that_was_never_kept_is_simply_absent()
    {
        Assert.SkipWhen(Here is null, "nothing installed on this machine can hold a secret.");

        Here.Recall(Absent).ShouldBeNull();
    }

    [Fact]
    public void Forgetting_a_secret_removes_it()
    {
        Assert.SkipWhen(Here is null, "nothing installed on this machine can hold a secret.");

        var store = Here;

        store.Keep(Account, "sk-briefly");
        store.Forget(Account);

        store.Recall(Account).ShouldBeNull();
    }

    [Fact]
    public void Forgetting_something_that_was_never_there_is_not_an_error()
    {
        Assert.SkipWhen(Here is null, "nothing installed on this machine can hold a secret.");

        Should.NotThrow(() => Here.Forget(Absent));
    }

    /// <summary>
    /// A store for another operating system refuses when it is asked to work,
    /// rather than half-working against a folder or a program that means
    /// something else here. The guard this pins is the only thing standing
    /// between a plugin that loads everywhere — which is the point of
    /// <see cref="ISecretStore.IsSupported"/> — and one that would write a key
    /// somewhere nobody meant.
    /// </summary>
    [Theory]
    [MemberData(nameof(PlatformStores))]
    public void A_store_for_another_system_refuses_rather_than_pretending(string id)
    {
        Assert.SkipWhen(BelongsHere(id), "this is the store for this machine; the round trip covers it.");

        var store = Store(id);

        Should.Throw<PlatformNotSupportedException>(() => store.Keep(Account, "sk-going-nowhere"));
        Should.Throw<PlatformNotSupportedException>(() => store.Recall(Account));
        Should.Throw<PlatformNotSupportedException>(() => store.Forget(Account));
    }

    /// <summary>
    /// Windows only, because it is the only one of the three that writes a file
    /// of its own — the other two hand the key to something that decides where
    /// it goes. What is on disk here is what <c>ProtectedData</c> handed back,
    /// and this is what says so.
    /// </summary>
    [Fact]
    public void What_windows_writes_to_disk_is_not_the_key()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows data protection needs Windows.");

        var store = Store("dpapi");
        const string secret = "sk-a-value-that-is-not-a-real-key";

        try
        {
            store.Keep(Account, secret);

            Written().ShouldNotContain(secret);
        }
        finally
        {
            store.Forget(Account);
        }
    }

    /// <summary>Whether a store id is the one for the machine these tests are running on.</summary>
    private static bool BelongsHere(string id) => id switch
    {
        "dpapi" => OperatingSystem.IsWindows(),
        "keychain" => OperatingSystem.IsMacOS(),
        "secret-service" => OperatingSystem.IsLinux(),
        _ => false,
    };

    /// <summary>What the Windows store has on disk under that account name, or empty if there is nothing.</summary>
    private static string Written()
    {
        var file = Path.Combine(GlobalConstants.DataFolder, "secrets", Account + ".bin");

        return File.Exists(file) ? System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)) : string.Empty;
    }
}

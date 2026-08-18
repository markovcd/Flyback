using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The store that keeps a key between runs. This is the one place where a bug
/// is a disclosure rather than a glitch, so it is tested against the real thing
/// rather than a stand-in.
/// </summary>
public class SecretStoreTests
{
    /// <summary>Distinctive, so a stray blob is obviously the tests' and not somebody's key.</summary>
    private const string Account = "flyback-test-account";

    private static ISecretStore Dpapi =>
        PluginHost.Load().SecretStores.Single(s => s.Id == "dpapi");

    [Fact]
    public void A_store_in_a_plugin_reaches_the_catalogue()
    {
        PluginHost.Load().SecretStores.Select(s => s.Id).ShouldContain("dpapi");
    }

    /// <summary>
    /// Asserted in the direction that is knowable, the way the ALSA test is:
    /// on Windows it must work, and elsewhere it must say no rather than break.
    /// </summary>
    [Fact]
    public void Windows_data_protection_is_offered_only_on_windows()
    {
        Dpapi.IsSupported.ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void Being_asked_whether_it_works_here_never_throws()
    {
        foreach (var store in PluginHost.Load().SecretStores)
            Should.NotThrow(() => store.IsSupported);
    }

    /// <summary>
    /// The one that matters: a key put away comes back, and the file it went
    /// into is not the key.
    /// </summary>
    [Fact]
    public void A_secret_survives_being_put_away_and_asked_for()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows data protection needs Windows.");

        var store = Dpapi;
        const string secret = "sk-a-value-that-is-not-a-real-key";

        try
        {
            store.Keep(Account, secret);

            store.Recall(Account).ShouldBe(secret);
            Written().ShouldNotContain(secret);
        }
        finally
        {
            store.Forget(Account);
        }
    }

    [Fact]
    public void A_secret_that_was_never_kept_is_simply_absent()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows data protection needs Windows.");

        Dpapi.Recall("flyback-test-account-that-does-not-exist").ShouldBeNull();
    }

    [Fact]
    public void Forgetting_a_secret_removes_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows data protection needs Windows.");

        var store = Dpapi;

        store.Keep(Account, "sk-briefly");
        store.Forget(Account);

        store.Recall(Account).ShouldBeNull();
    }

    [Fact]
    public void Forgetting_something_that_was_never_there_is_not_an_error()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows data protection needs Windows.");

        Should.NotThrow(() => Dpapi.Forget("flyback-test-account-that-does-not-exist"));
    }

    /// <summary>What is on disk under that account name, or empty if there is nothing.</summary>
    private static string Written()
    {
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Flyback",
            "secrets",
            Account + ".bin");

        return File.Exists(file) ? System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)) : string.Empty;
    }
}

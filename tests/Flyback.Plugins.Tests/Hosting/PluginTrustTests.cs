using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>Which plugin folders load: what Flyback shipped, and what somebody allowed, each as its files stand.</summary>
public sealed class PluginTrustTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-trust-{Guid.NewGuid():N}")).FullName;

    private string Plugins => Path.Combine(root, PluginHost.DirectoryName);

    private PluginAllowances Allowances => new(Path.Combine(root, "allowed-plugins.json"));

    /// <summary>What a test loaded stays loaded, and Windows keeps its files open until the process ends.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void A_folder_nobody_allowed_is_not_loaded_and_says_how_to_allow_it()
    {
        var folder = CopiedIn("Bare", "Scripted");

        var catalog = PluginHost.Load(Plugins, Checked());

        catalog.Plugins.ShouldBeEmpty();

        var problem = catalog.Problems.ShouldHaveSingleItem();
        problem.Folder.ShouldBe(folder);
        problem.Message.ShouldStartWith("not yet allowed");
        problem.Message.ShouldContain($"flyback-cli plugin allow \"{folder}\"");
    }

    [Fact]
    public void A_build_that_checks_nothing_loads_the_same_folder()
    {
        CopiedIn("Bare", "Scripted");

        var catalog = PluginHost.Load(Plugins, PluginTrust.Unchecked);

        catalog.Plugins.ShouldHaveSingleItem().Info.Id.ShouldBe("flyback.bare");
        catalog.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void An_allowed_folder_loads_until_a_file_in_it_changes()
    {
        var folder = CopiedIn("Bare", "Scripted");

        Allowances.Allow(folder, secrets: false);

        Checked().Judge(folder).ShouldBe(new PluginVerdict(PluginStanding.Allowed, false, null));

        File.AppendAllText(Path.Combine(folder, "Flyback.Plugins.Bare.dll"), "tampered");

        var catalog = PluginHost.Load(Plugins, Checked());

        catalog.Plugins.ShouldBeEmpty();
        catalog.Problems.ShouldHaveSingleItem().Message
            .ShouldStartWith("Flyback.Plugins.Bare.dll has changed since this plugin was allowed");
    }

    [Fact]
    public void A_file_added_to_an_allowed_folder_is_a_change_too()
    {
        var folder = CopiedIn("Bare", "Scripted");

        Allowances.Allow(folder, secrets: false);
        File.WriteAllText(Path.Combine(folder, "Extra.dll"), "anything");

        Checked().Judge(folder).Standing.ShouldBe(PluginStanding.Changed);
    }

    [Fact]
    public void A_name_starting_with_a_dot_is_not_part_of_what_was_allowed()
    {
        var folder = CopiedIn("Bare", "Scripted");

        Allowances.Allow(folder, secrets: false);
        File.WriteAllText(Path.Combine(folder, ".DS_Store"), "Finder was here");

        Checked().Judge(folder).Standing.ShouldBe(PluginStanding.Allowed);
    }

    [Fact]
    public void A_denied_folder_is_not_loaded_again()
    {
        var folder = CopiedIn("Bare", "Scripted");

        Allowances.Allow(folder, secrets: false);
        Allowances.Deny(folder).ShouldBeTrue();
        Allowances.Deny(folder).ShouldBeFalse();

        Checked().Judge(folder).Standing.ShouldBe(PluginStanding.NotAllowed);
    }

    [Fact]
    public void Every_plugin_the_build_laid_out_loads_on_its_list_alone()
    {
        var catalog = PluginHost.Load(
            PluginHost.DefaultDirectory,
            new PluginTrust(true, ShippedList.Beside(PluginHost.DefaultDirectory), PluginAllowances.None));

        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Count.ShouldBe(ShippedPlugins.Loaded.Plugins.Count);
        catalog.SecretStores.ShouldNotBeEmpty("a shipped plugin may keep keys");
    }

    [Fact]
    public void A_shipped_folder_whose_file_was_replaced_is_not_loaded()
    {
        var folder = CopiedIn("Bare", "Bare");
        var list = ShippedList.Parse(string.Concat(
            PluginFiles.Of(folder).Hashes.Select(f => $"{f.Value}  Bare/{f.Key}\n")));

        File.AppendAllText(Path.Combine(folder, "Flyback.Plugins.Bare.dll"), "tampered");

        var verdict = new PluginTrust(true, list, Allowances).Judge(folder);

        verdict.Standing.ShouldBe(PluginStanding.Changed);
        verdict.Reason.ShouldBe("Flyback.Plugins.Bare.dll is not as Flyback shipped it, so this plugin is not loaded.");
    }

    [Fact]
    public void An_allowed_plugin_without_the_grant_keeps_no_keys()
    {
        var folder = CopiedIn("Dpapi", "Keys");

        Allowances.Allow(folder, secrets: false);

        var catalog = PluginHost.Load(Plugins, Checked());

        catalog.Plugins.ShouldHaveSingleItem();
        catalog.SecretStores.ShouldBeEmpty();
        catalog.Problems.ShouldHaveSingleItem().Message.ShouldContain("secret store 'dpapi' is refused");
    }

    [Fact]
    public void An_allowed_plugin_with_the_grant_keeps_keys()
    {
        var folder = CopiedIn("Dpapi", "Keys");

        Allowances.Allow(folder, secrets: true);

        PluginHost.Load(Plugins, Checked()).SecretStores.ShouldHaveSingleItem().Id.ShouldBe("dpapi");
    }


    [Fact]
    public void Two_plugins_offering_one_secret_store_both_lose_it()
    {
        var catalog = PluginHost.LoadTypes(typeof(FirstStore), typeof(SecondStore));

        catalog.Plugins.Count.ShouldBe(2);
        catalog.SecretStores.ShouldBeEmpty();
        catalog.PreferredSecretStore.ShouldBeNull();
        catalog.Problems.Select(p => p.ToString()).ShouldBe(
        [
            "test.first-store: secret store 'grabby' is registered by Second store as well, so neither is used.",
            "test.second-store: secret store 'grabby' is registered by First store as well, so neither is used.",
        ]);
    }

    [Fact]
    public void Folders_a_package_installed_are_adopted_once_per_plugins_folder()
    {
        var installed = CopiedIn("Bare", "Installed");
        var copied = CopiedIn("Sample", "Copied");

        File.WriteAllText(Path.Combine(installed, PluginPackage.MarkerName), "abc\n");

        Allowances.Adopt(Plugins);

        Checked().Judge(installed).Standing.ShouldBe(PluginStanding.Allowed);
        Checked().Judge(copied).Standing.ShouldBe(PluginStanding.NotAllowed);

        var later = CopiedIn("Bare", "Later");
        File.WriteAllText(Path.Combine(later, PluginPackage.MarkerName), "abc\n");

        Allowances.Adopt(Plugins);

        Checked().Judge(later).Standing.ShouldBe(PluginStanding.NotAllowed, "a folder that arrives after the first checked start is nobody's yes");
    }

    [Fact]
    public void A_broken_list_allows_nothing()
    {
        var folder = CopiedIn("Bare", "Scripted");

        File.WriteAllText(Allowances.File, "{ not json");

        Checked().Judge(folder).Standing.ShouldBe(PluginStanding.NotAllowed);
    }

    [Theory]
    [InlineData("""{ "adopted": [], "adopted": [], "plugins": [] }""")]
    [InlineData("""{ "plugins": [ { "folder": "/x", "folder": "/y", "files": {} } ] }""")]
    [InlineData("""{ "plugins": [ { "folder": "/x", "files": { "a.dll": "1", "a.dll": "2" } } ] }""")]
    public void A_list_with_a_key_given_twice_is_broken_and_allows_nothing(string json)
    {
        File.WriteAllText(Allowances.File, json);

        Allowances.All().ShouldBeEmpty();
        Should.NotThrow(() => Allowances.Adopt(Plugins));
    }

    private PluginTrust Checked() => new(true, ShippedList.Beside(Plugins), Allowances);

    /// <summary>A shipped plugin's folder copied under <paramref name="name"/>, as somebody would by hand.</summary>
    private string CopiedIn(string shipped, string name)
    {
        var from = Path.Combine(PluginHost.DefaultDirectory, shipped);
        var to = Path.Combine(Plugins, name);

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return to;
    }



    public sealed class FirstStore : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("test.first-store", "First store");

        public void Register(IPluginRegistry registry) => registry.AddSecretStore(new Store());
    }

    public sealed class SecondStore : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("test.second-store", "Second store");

        public void Register(IPluginRegistry registry) => registry.AddSecretStore(new Store());
    }


    private sealed class Store : ISecretStore
    {
        public string Id => "grabby";

        public string Name => "Grabby";

        public int Priority => int.MaxValue;

        public bool IsSupported => true;

        public void Keep(string account, string secret)
        {
        }

        public string? Recall(string account) => null;

        public void Forget(string account)
        {
        }
    }
}

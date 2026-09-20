using System.Runtime.Loader;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// Loads the sound plugins that this test project builds into its own output,
/// so what is exercised is a real separate assembly with real dependencies —
/// the part a stub plugin would not prove.
/// </summary>
public class PluginHostTests
{
    /// <summary>
    /// The plugins that ship in the box, one per operating system. Each carries
    /// that platform's sound backend and its MIDI one, so a plugin is named for
    /// the platform rather than for either backend inside it.
    /// </summary>
    public static TheoryData<string> PlatformPlugins => ["linux.io", "mac.io", "win.io"];

    /// <summary>
    /// The sound backends those plugins offer. Named for the interface each
    /// speaks rather than for the plugin carrying it: this is the id a log line
    /// and a setting hold on to.
    /// </summary>
    public static TheoryData<string> PlatformBackends => ["alsa", "coreaudio", "wasapi"];

    private static PluginCatalog Shipped() => PluginHost.Load();

    /// <summary>Which plugin each shipped backend was registered by, sound then MIDI.</summary>
    public static TheoryData<string, string> BackendPlugins => new()
    {
        { "alsa", "linux.io" },
        { "coreaudio", "mac.io" },
        { "wasapi", "win.io" },
        { "alsaseq", "linux.io" },
        { "coremidi", "mac.io" },
        { "winmm", "win.io" },
    };

    [Theory]
    [MemberData(nameof(PlatformPlugins))]
    public void The_shipped_plugin_is_found(string id)
    {
        var catalog = Shipped();

        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Select(p => p.Info.Id).ShouldContain(id);
    }

    [Theory]
    [MemberData(nameof(PlatformBackends))]
    public void A_found_plugin_registers_what_it_offers(string id)
    {
        var catalog = Shipped();

        catalog.AudioOutputs.Select(o => o.Id).ShouldContain(id);
    }

    /// <summary>
    /// The failure this guards is silent: load the contract a second time inside
    /// the plugin's context and its types no longer match the host's, so a
    /// perfectly good plugin simply stops being one.
    /// </summary>
    [Fact]
    public void The_contract_keeps_one_identity_across_the_boundary()
    {
        var output = Shipped().AudioOutputs.Single(o => o.Id == "wasapi");

        // Loaded from somewhere other than the host's own context …
        var context = AssemblyLoadContext.GetLoadContext(output.GetType().Assembly);
        context.ShouldNotBe(AssemblyLoadContext.Default);

        // … yet the contract it implements is the very type this assembly compiled against.
        typeof(IAudioOutput).IsInstanceOfType(output).ShouldBeTrue();
        AssemblyLoadContext.GetLoadContext(typeof(IAudioOutput).Assembly)
            .ShouldBe(AssemblyLoadContext.Default);
    }

    /// <summary>
    /// All three plugins load everywhere; only their devices are tied to one system.
    /// That split is the whole point of separating <see cref="IAudioOutput"/> from
    /// <see cref="IAudioDevice"/>.
    /// </summary>
    /// <remarks>
    /// ALSA is asserted in one direction only: off Linux the answer is a flat no, and
    /// on Linux it also depends on whether libasound is installed — which a test that
    /// decided for itself would be asserting its own copy of.
    /// </remarks>
    [Fact]
    public void Support_is_answered_without_opening_a_device()
    {
        var outputs = Shipped().AudioOutputs;

        outputs.Single(o => o.Id == "wasapi").IsSupported.ShouldBe(OperatingSystem.IsWindows());
        outputs.Single(o => o.Id == "coreaudio").IsSupported.ShouldBe(OperatingSystem.IsMacOS());

        if (!OperatingSystem.IsLinux())
            outputs.Single(o => o.Id == "alsa").IsSupported.ShouldBeFalse();
    }

    /// <summary>
    /// The three native backends claim the same priority, which is only safe
    /// because no machine supports two of them. If that ever stopped being true
    /// the choice would fall to the tie-break on id and Windows would quietly
    /// start preferring ALSA, so pin the whole selection rather than the pieces.
    /// </summary>
    [Fact]
    public void The_backend_chosen_here_is_the_one_for_this_operating_system()
    {
        var chosen = Shipped().PreferredAudioOutput?.Id;

        // On Linux, either answer is correct and which one is a property of the
        // machine: ALSA where libasound is installed, and otherwise nothing —
        // which is what leaves the Audio button disabled rather than opening a
        // device that cannot exist.
        if (OperatingSystem.IsLinux())
        {
            (chosen is null or "alsa").ShouldBeTrue($"chose '{chosen}'");
            return;
        }

        chosen.ShouldBe(OperatingSystem.IsWindows() ? "wasapi" : OperatingSystem.IsMacOS() ? "coreaudio" : null);
    }

    /// <summary>
    /// Each native backend asks one thing, which device plays, and starts on whatever
    /// the system is playing through. Where it is not supported it asks nothing, and
    /// never reaches for a library the machine does not have to find that out.
    /// </summary>
    [Theory]
    [MemberData(nameof(PlatformBackends))]
    public void A_native_backend_asks_which_device_plays(string id)
    {
        var output = Shipped().AudioOutputs.Single(o => o.Id == id);
        var form = output.Form(SettingValues.None);

        if (!output.IsSupported)
        {
            form.ShouldBeEmpty();
            return;
        }

        var device = form.ShouldHaveSingleItem().ShouldBeOfType<SettingField.Pick>();

        device.Key.ShouldBe("device");
        device.Sane(null).ShouldBe("default");
        device.Options[0].Id.ShouldBe("default");
        device.Note.ShouldBeNull();
    }

    /// <summary>
    /// A device picked and then unplugged stays picked, so plugging it back in is
    /// all it takes — and the form says what plays meanwhile rather than showing an
    /// id nobody could read.
    /// </summary>
    [Fact]
    public void A_device_that_has_gone_stays_chosen_and_says_so()
    {
        if (!OperatingSystem.IsWindows()) return;

        var output = Shipped().AudioOutputs.Single(o => o.Id == "wasapi");
        var gone = SettingValues.None.With("device", "{0.0.0.00000000}.{not-plugged-in}");

        var device = output.Form(gone).ShouldHaveSingleItem().ShouldBeOfType<SettingField.Pick>();

        device.Sane(gone.Text("device")).ShouldBe("{0.0.0.00000000}.{not-plugged-in}");
        device.Name("{0.0.0.00000000}.{not-plugged-in}").ShouldNotContain("{");
        device.Note.ShouldNotBeNull();

        // Creating opens nothing, so a device that is not there cannot fail it.
        using var created = output.Create(AudioFormat.Default, gone);
    }

    [Fact]
    public void A_missing_directory_is_not_an_error()
    {
        var catalog = PluginHost.Load(Path.Combine(Path.GetTempPath(), $"flyback-absent-{Guid.NewGuid():N}"));

        catalog.Plugins.ShouldBeEmpty();
        catalog.AudioOutputs.ShouldBeEmpty();
        catalog.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_directory_is_not_an_error()
    {
        using var folder = new TempFolder();

        PluginHost.Load(folder.Path).Plugins.ShouldBeEmpty();
    }

    /// <summary>
    /// A folder holding something that is not a managed assembly at all — a
    /// native library, or a half-written download. It must not stop the rest of
    /// the scan, and it must not throw.
    /// </summary>
    [Fact]
    public void Rubbish_in_a_plugin_folder_is_ignored()
    {
        using var folder = new TempFolder();

        Directory.CreateDirectory(Path.Combine(folder.Path, "Broken"));
        File.WriteAllBytes(Path.Combine(folder.Path, "Broken", "not-really.dll"), [0x00, 0x01, 0x02, 0x03]);

        var catalog = PluginHost.Load(folder.Path);

        catalog.Plugins.ShouldBeEmpty();
        catalog.AudioOutputs.ShouldBeEmpty();
    }

    /// <summary>
    /// A plugin folder with nothing in it at all is a common half-state — an
    /// uninstall that left the directory behind.
    /// </summary>
    [Fact]
    public void An_empty_plugin_folder_is_ignored()
    {
        using var folder = new TempFolder();

        Directory.CreateDirectory(Path.Combine(folder.Path, "Nothing"));

        PluginHost.Load(folder.Path).Problems.ShouldBeEmpty();
    }

    /// <summary>
    /// A backend goes by its own name — "WASAPI (shared mode)" — which says
    /// nothing about what to install or uninstall to change it. The settings
    /// window names the plugin beside it, and this is where it reads that from.
    /// </summary>
    [Theory]
    [MemberData(nameof(BackendPlugins))]
    public void A_backend_knows_which_plugin_registered_it(string backend, string plugin)
    {
        var catalog = Shipped();

        object offered = catalog.AudioOutputs.FirstOrDefault(o => o.Id == backend)
            ?? (object?)catalog.MidiInputs.FirstOrDefault(i => i.Id == backend)
            ?? throw new InvalidOperationException($"no backend '{backend}' was registered.");

        catalog.Provider(offered)!.Id.ShouldBe(plugin);
    }

    /// <summary>
    /// Asked about something it never saw registered — a backend from another
    /// catalogue, or one a test made up — it says so rather than guessing.
    /// </summary>
    [Fact]
    public void Something_no_plugin_registered_has_no_provider()
    {
        Shipped().Provider(new object()).ShouldBeNull();
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } =
            Directory.CreateDirectory(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flyback-plugins-{Guid.NewGuid():N}")).FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

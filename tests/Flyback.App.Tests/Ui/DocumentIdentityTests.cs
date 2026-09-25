using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// What the window believes it has open, across the routes that change it.
/// </summary>
/// <remarks>
/// A document is more than the patch on the canvas: it is also what it is
/// called, which kind of file it goes back to, and where the sounds and pictures
/// it names are looked up. Those travel together or not at all — a route that
/// changes some of them leaves the window answering for a file nobody has open,
/// and the answer it gives is a wrong sound rather than an error.
/// </remarks>
public class DocumentIdentityTests : UiTest
{

    private static ComboBox PresetList(MainWindow window) => All<ComboBox>(window)
        .First(box => box.ItemsSource?.OfType<PatchPreset>().Any(p => p.Name == "Plasma") == true);

    /// <summary>As though a bundle had been opened, which no picker will do here.</summary>
    private static void OpenABundle(MainWindow window) =>
        window.Became("nebula", beside: null, new BundleFiles(new Dictionary<string, byte[]>()));

    [AvaloniaFact]
    public void A_bundle_is_the_document_until_something_else_is()
    {
        var window = Open();

        window.IsBundle.ShouldBeFalse("the window opens on a preset");

        OpenABundle(window);

        // The title is not checked here: naming a document does not redraw it, which
        // is why Became has to be called before the patch it is about.
        window.IsBundle.ShouldBeTrue();
    }

    /// <summary>
    /// A preset is not the bundle that was open, and the sounds it names are not
    /// that bundle's sounds.
    /// </summary>
    /// <remarks>
    /// What a patch names is looked up in the files a bundle carried before
    /// anywhere else, so a preset that did not disown them would read a sound
    /// out of a document nobody has open — silently, and with a file that is
    /// genuinely there to be found.
    /// </remarks>
    [AvaloniaFact]
    public void A_preset_disowns_the_bundle_that_was_open()
    {
        var window = Open();

        OpenABundle(window);

        Pick(PresetList(window), "Kaleidoscope");
        Dispatcher.UIThread.RunJobs();

        window.IsBundle.ShouldBeFalse("a preset came out of no file at all");
    }

    /// <summary>
    /// A built preset that carries files is a bundle of them: they are what it plays,
    /// and a save keeps them with it.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_that_carries_files_is_a_bundle_of_them()
    {
        var speaking = new PatchPreset("Speaking", _ => new Patch())
        {
            Files = () => new Dictionary<string, byte[]> { ["line.wav"] = [] },
        };

        var window = Open(setup: new EditorSetup
        {
            Plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Presets.All, speaking], []),
        });

        Pick(PresetList(window), "Speaking");
        Dispatcher.UIThread.RunJobs();

        window.IsBundle.ShouldBeTrue();

        Pick(PresetList(window), "Kaleidoscope");
        Dispatcher.UIThread.RunJobs();

        window.IsBundle.ShouldBeFalse("a preset that carries nothing is not a bundle");
    }

    /// <summary>
    /// A file opened from disk is not any row of the preset list, so nothing there
    /// should still look picked once one is open.
    /// </summary>
    /// <remarks>
    /// The window opens on the list's first row and a bundle carries no selection of
    /// its own, so without this, opening one left that preset looking chosen. Driven
    /// through <see cref="MainWindow.ClearPresetSelection"/> rather than a real Open
    /// dialog, which the headless platform does not put up.
    /// </remarks>
    [AvaloniaFact]
    public void Opening_a_file_takes_the_selection_off_the_preset_list()
    {
        var window = Open();

        PresetList(window).SelectedIndex.ShouldBeGreaterThanOrEqualTo(0, "the window opens on a preset");

        OpenABundle(window);
        window.ClearPresetSelection();
        Dispatcher.UIThread.RunJobs();

        PresetList(window).SelectedIndex.ShouldBe(-1);
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Composition;
using Flyback.Editor.Shell;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Shell;

/// <summary>What each place offers is settled in <see cref="EditorHost"/>, and the toolbar follows it.</summary>
public sealed class EditorPlaceTests : EditorTest
{
    private static string Offers(EditorHost host) => string.Join(' ', new (string Name, bool On)[]
    {
        ("files", host.OpensFiles),
        ("record", host.Records),
        ("measure", host.Measures),
        ("assistant", host.Assists),
        ("about", host.ShowsAbout),
        ("plugins", host.InstallsPlugins),
        ("settings-window", host.SettingsWindow),
        ("viewer", host.OffersViewer),
    }.Where(c => c.On).Select(c => c.Name));

    [AvaloniaTheory]
    [InlineData(EditorPlace.Desktop, "files record measure assistant about plugins settings-window")]
    [InlineData(EditorPlace.Device, "files assistant about settings-window")]
    [InlineData(EditorPlace.Page, "viewer")]
    public void Each_place_offers_what_it_can(EditorPlace place, string offers) =>
        Offers(new EditorHost { Place = place }).ShouldBe(offers);

    [AvaloniaFact]
    public void Unset_is_a_desktop() => new EditorHost().Place.ShouldBe(EditorPlace.Desktop);

    [AvaloniaTheory]
    [InlineData(EditorPlace.Desktop)]
    [InlineData(EditorPlace.Device)]
    [InlineData(EditorPlace.Page)]
    public void The_toolbar_has_a_button_for_each_thing_offered(EditorPlace place)
    {
        var host = new EditorHost { Place = place };
        var window = NewMainWindow(new EditorSetup { Host = host });

        window.Show();
        Settle(window);

        var names = All<Button>(window).Select(b => b.Name).OfType<string>().ToHashSet();

        names.Contains("open").ShouldBe(host.OpensFiles);
        names.Contains("save").ShouldBe(host.OpensFiles);
        names.Contains("record").ShouldBe(host.Records);
        names.Contains("measure").ShouldBe(host.Measures);
        names.Contains("about").ShouldBe(host.ShowsAbout);
        names.Contains("plugins").ShouldBe(host.InstallsPlugins);
        names.Contains("view-it").ShouldBe(host.OffersViewer);
    }
}

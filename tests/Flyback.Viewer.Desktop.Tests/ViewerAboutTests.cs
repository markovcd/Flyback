using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Ui.Testing;
using Shouldly;

namespace Flyback.Viewer.Desktop.Tests;

/// <summary>The window the viewer's macOS menu opens: which program, and which build.</summary>
public class ViewerAboutTests : UiTest
{
    [AvaloniaFact]
    public void It_names_the_viewer_and_its_version()
    {
        var window = Owned(new ViewerAbout());

        window.Show();
        Settle(window);

        var said = All<TextBlock>(window).Select(t => t.Text).ToList();

        said.ShouldContain(ViewerApp.Title);
        said.ShouldContain($"Version {ViewerAbout.Version}");
    }
}

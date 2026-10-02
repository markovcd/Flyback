using System.Runtime.InteropServices.JavaScript;
using Flyback.Editor;

namespace Flyback.WebEditor;

/// <summary>The web viewer, in a tab of its own beside the editor's, playing the patch from a blob the editor holds.</summary>
internal sealed partial class PageViewer(ReportLine report) : IViewer
{
    /// <summary>The viewer's address as it was last opened, or null where the browser refused the tab.</summary>
    public string? Opened { get; private set; }

    public void Show(string name, Func<byte[]> pack)
    {
        // The tab before the packing, while the press still lets the page open one.
        if (!OpenTab())
        {
            Opened = null;
            report.Say("The browser would not open the viewer's tab. Let this site open pop-ups, and press View it again.");
            return;
        }

        Opened = View(pack(), name);
        report.Say($"Viewing “{name}” in its own tab.");
    }

    [JSImport("openViewerTab", PageModule.Name)]
    private static partial bool OpenTab();

    [JSImport("view", PageModule.Name)]
    private static partial string View(byte[] bundle, string name);
}

using Avalonia.Controls;
using Flyback.App.Controls;
using Flyback.App.Statistics;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// What every region of the window reads: the window itself, the canvas, the
/// document, the plugins, the report line, the usage counts and the assistant
/// (ADR-0148).
/// </summary>
/// <remarks>
/// Built once, by the container, and handed to each region in place of the same six
/// or seven arguments. The window is the one thing here the container cannot hand
/// over, since the window is built from the regions: it attaches itself as the first
/// thing its constructor does, and a region reads <see cref="Owner"/> only once it is
/// asked to do something (ADR-0150).
/// </remarks>
internal sealed class Shell(
    NodeEditor editor,
    Document document,
    PluginCatalog plugins,
    ReportLine report,
    Usage usage,
    Lazy<AssistantPanel> assistant)
{
    private Window? owner;

    /// <summary>The window, for a dialog, a file picker or the monitors it is on.</summary>
    public Window Owner => owner ?? throw new InvalidOperationException("The window has not attached itself to its shell yet.");

    public NodeEditor Editor { get; } = editor;

    public Document Document { get; } = document;

    public PluginCatalog Plugins { get; } = plugins;

    /// <summary>The one line anything is said on.</summary>
    public ReportLine Report { get; } = report;

    public Usage Usage { get; } = usage;

    /// <summary>The assistant's column, or null where there is none.</summary>
    public AssistantPanel? Assistant => assistant.Value;

    /// <summary>Says which window the regions are in. Once, by that window, before it builds anything.</summary>
    public void Attach(Window window) => owner = window;
}

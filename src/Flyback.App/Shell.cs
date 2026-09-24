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
/// Built once, by the window, and handed to each region in place of the same six
/// or seven arguments. A region still takes whatever else it needs on its own.
/// </remarks>
internal sealed class Shell(
    Window owner,
    NodeEditor editor,
    Document document,
    PluginCatalog plugins,
    ReportLine report,
    Usage usage,
    Func<AssistantPanel?> assistant)
{
    /// <summary>The window, for a dialog, a file picker or the monitors it is on.</summary>
    public Window Owner { get; } = owner;

    public NodeEditor Editor { get; } = editor;

    public Document Document { get; } = document;

    public PluginCatalog Plugins { get; } = plugins;

    /// <summary>The one line anything is said on.</summary>
    public ReportLine Report { get; } = report;

    public Usage Usage { get; } = usage;

    /// <summary>The assistant's column, or null before the layout is built or where there is none.</summary>
    public AssistantPanel? Assistant => assistant();
}

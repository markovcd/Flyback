using Avalonia.Controls;
using Flyback.App.Controls;
using Flyback.App.Statistics;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// What every region of the window reads: the canvas, the document, the plugins,
/// the report line, the usage counts and the assistant (ADR-0148).
/// </summary>
/// <remarks>
/// Built once, by the container, and handed to each region in place of the same six
/// or seven arguments. What a region asks of the window itself is a service of its
/// own: <see cref="IDialogs"/>, <see cref="IFilePickers"/> and the rest (ADR-0150).
/// </remarks>
internal sealed class Shell(
    NodeEditor editor,
    Document document,
    PluginCatalog plugins,
    ReportLine report,
    Usage usage,
    Lazy<AssistantPanel> assistant)
{
    public NodeEditor Editor { get; } = editor;

    public Document Document { get; } = document;

    public PluginCatalog Plugins { get; } = plugins;

    /// <summary>The one line anything is said on.</summary>
    public ReportLine Report { get; } = report;

    public Usage Usage { get; } = usage;

    /// <summary>The assistant's column, or null where there is none.</summary>
    public AssistantPanel? Assistant => assistant.Value;
}

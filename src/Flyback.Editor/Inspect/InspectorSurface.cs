using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flyback.Editor.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The controls the inspector draws on: the wash behind the column, the plate heading the
/// rows, the rows, and the header that stands in for the plate once it has scrolled away.
/// </summary>
internal sealed class InspectorSurface
{
    /// <summary>
    /// How far the panel's rows keep off its edges. Named because the plate at the
    /// head of it takes the inset back off again to reach them.
    /// </summary>
    internal const double PanelInset = 12;

    /// <summary>
    /// Named so a test can find it. It is the one panel here that is switched
    /// off whole while the text owns the patch, and there is nothing else about it
    /// to tell it apart by.
    /// </summary>
    public StackPanel Panel { get; } = new()
    {
        Name = "inspector",
        Margin = new Thickness(PanelInset),
        Spacing = 8,
    };

    /// <summary>
    /// The selected block's background, behind everything on the panel and fading
    /// out down it, with the block's mark set large in it.
    /// </summary>
    public ModuleWash Wash { get; } = new();

    /// <summary>Where the plate stands: at the head of the rows, scrolling with them.</summary>
    public ContentControl PlateHost { get; } = new() { Name = "plate-host" };

    /// <summary>The plate folded to one pinned line, shown where the plate is not.</summary>
    public InspectorHeader Header { get; } = new();

    public InspectorSurface(TextWriteBack writeBack, Renamer renamer)
    {
        Header.Renamer = renamer;

        // A drag on a slider is one edit, written into the text once the hand is off it.
        Panel.AddHandler(InputElement.PointerReleasedEvent, (_, _) => writeBack.HandCameOff(), RoutingStrategies.Bubble, handledEventsToo: true);
        Panel.AddHandler(InputElement.LostFocusEvent, (_, _) => writeBack.HandCameOff(), RoutingStrategies.Bubble);
        Panel.AddHandler(InputElement.KeyUpEvent, (_, _) => writeBack.HandCameOff(), RoutingStrategies.Bubble, handledEventsToo: true);
        Panel.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => writeBack.HandCameOff(), RoutingStrategies.Bubble, handledEventsToo: true);
    }
}

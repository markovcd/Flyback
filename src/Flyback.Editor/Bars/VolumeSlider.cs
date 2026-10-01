using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Notices;
using Flyback.Core.Graph;

namespace Flyback.App.Bars;

/// <summary>
/// The Output's Volume on the transport row, turned as its knob on the panel is: an edit,
/// one step a drag or a notch of the wheel. Grayed out while a wire or a panel knob drives it.
/// </summary>
internal sealed class VolumeSlider : IReactTo<PatchChanged>
{
    public const string Tip = "How loud the speakers are: the Output's Volume. All the way down closes them.";

    private const int Port = NodeCatalog.OutputVolumePort;

    private readonly NodeEditor editor;
    private readonly Reactions reactions;

    /// <summary>Set while the slider is being put where the patch says, so that is not taken for a turn.</summary>
    private bool following;

    /// <summary>Whether a hand has moved it since it last came off.</summary>
    private bool turned;

    public VolumeSlider(NodeEditor editor, Reactions reactions)
    {
        this.editor = editor;
        this.reactions = reactions;

        var spec = NodeCatalog.Get(NodeCatalog.OutputTypeId)!.Inputs[Port];

        Slider = new Slider
        {
            Name = "volume",
            Minimum = spec.Min,
            Maximum = spec.Max,
            Width = 90,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetShowOnDisabled(Slider, true);

        Slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && e.NewValue is double value && !following) Turn((float)value);
        };

        Slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => LetGo(), RoutingStrategies.Bubble, handledEventsToo: true);
        Slider.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => LetGo(), RoutingStrategies.Bubble, handledEventsToo: true);
        Slider.AddHandler(InputElement.KeyUpEvent, (_, _) => LetGo(), RoutingStrategies.Bubble, handledEventsToo: true);
        Slider.AddHandler(InputElement.LostFocusEvent, (_, _) => LetGo(), RoutingStrategies.Bubble);

        var speaker = Glyphs.Speaker();
        speaker.VerticalAlignment = VerticalAlignment.Center;

        View = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Children = { speaker, Slider },
        };

        View.PointerWheelChanged += (_, e) =>
        {
            if (!Slider.IsEffectivelyEnabled) return;

            Slider.Value = Math.Clamp(Slider.Value + e.Delta.Y * WheelStep * (Slider.Maximum - Slider.Minimum), Slider.Minimum, Slider.Maximum);
            LetGo();
            e.Handled = true;
        };
    }

    /// <summary>How much of the slider's range one notch of a mouse wheel turns.</summary>
    public const double WheelStep = 0.05;

    /// <summary>The speaker and the slider.</summary>
    public StackPanel View { get; }

    /// <summary>The slider itself.</summary>
    public Slider Slider { get; }

    /// <summary>Follows the patch, however it changed: the panel, an undo, another patch opened.</summary>
    public Task On(PatchChanged notice)
    {
        Follow();
        return Task.CompletedTask;
    }

    private void Follow()
    {
        var patch = editor.History.Patch;

        // Before the first patch is opened, the canvas holds an empty one.
        if (patch.FirstOf(NodeCatalog.OutputTypeId) is not { } output) return;

        // Nothing wired into the sound has nothing to turn up.
        View.IsVisible = patch.Reaches().Sound;

        following = true;
        Slider.Value = output.InputValues[Port];
        following = false;

        var knob = ControlMap.Of(output, Port) is { } link ? patch.Control(link.Control) : null;
        var wired = patch.IncomingTo(output.Id, Port) is not null;

        Slider.IsEnabled = !wired && knob is null;

        ToolTip.SetTip(Slider, wired ? "A wire into the Output's Volume sets how loud the speakers are."
            : knob is not null ? $"The knob '{knob.Name}' on the knob panel sets how loud the speakers are."
            : Tip);
    }

    private void Turn(float value)
    {
        var output = editor.History.Patch.Output;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (output.InputValues[Port] == value) return;

        output.InputValues[Port] = value;
        turned = true;

        // The inspector's key for the same socket, so a drag here is one step to undo.
        editor.History.Record($"{output.Id} input {Port}");
        reactions.Raise(new InputTurned(new SocketPick(output.Id, Port)));
    }

    private void LetGo()
    {
        if (!turned) return;

        turned = false;
        reactions.Raise(new InputLetGo(new SocketPick(editor.History.Patch.Output.Id, Port)));
    }
}

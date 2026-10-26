using Avalonia.Controls;
using Avalonia.Layout;
using Flyback.Editor.Controls;
using Flyback.Editor.Settings;
using Flyback.Host;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Windows;

/// <summary>
/// The Picture section's rows for live patching: how much of the picture shows through
/// the modules and behind the text, and when each fades while left alone.
/// </summary>
internal sealed class LivePatchingRows
{
    private readonly LivePatching live;

    public Control View => rows;

    private readonly StackPanel rows = new() { Spacing = 8 };

    private readonly NumericUpDown moduleOpacity = Number("moduleOpacity", LivePatchingLook.Faintest, LivePatchingLook.Solid, 5);

    private readonly NumericUpDown textOpacity = Number("textOpacity", LivePatchingLook.Clear, LivePatchingLook.Solid, 5);

    private readonly NumericUpDown moduleFade = Number("moduleFade", 0, LivePatchingLook.LongestWait, 1);

    private readonly NumericUpDown textFade = Number("textFade", 0, LivePatchingLook.LongestWait, 1);

    /// <summary>What brings faded text back: the keys and the pointer, or the keys alone.</summary>
    private readonly ComboBox textWakes = new Picker
    {
        Name = "textWakes",
        ItemsSource = new[] { "Keys and pointer", "Keys only" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public LivePatchingRows(LivePatching live)
    {
        this.live = live;

        ToolTip.SetTip(moduleOpacity,
            "How solid the modules' fills, the boxes and the wires are over the picture, in percent. "
            + "Their text is always drawn in full, and a module whose value is in hand stays solid.");
        ToolTip.SetTip(textOpacity,
            "How dark the band behind each line of the text is over the picture, in percent; 0 is no band.");
        ToolTip.SetTip(moduleFade, "Seconds the modules are left alone before they fade away; 0 never fades them.");
        ToolTip.SetTip(textFade, "Seconds the text is left alone before it fades away; 0 never fades it.");
        ToolTip.SetTip(textWakes,
            "What brings faded text back. Keys only leaves it hidden while the mouse moves over the picture.");

        rows.Children.Add(InspectorRows.Field("Live modules, % solid", moduleOpacity));
        rows.Children.Add(InspectorRows.Field("Live modules fade, s", moduleFade));
        rows.Children.Add(InspectorRows.Field("Live text band, % solid", textOpacity));
        rows.Children.Add(InspectorRows.Field("Live text fades, s", textFade));
        rows.Children.Add(InspectorRows.Field("Live text wakes on", textWakes));
    }

    public void Show(OutputSettings current)
    {
        var look = current.Live;

        moduleOpacity.Value = look.ModuleOpacity;
        textOpacity.Value = look.TextOpacity;
        moduleFade.Value = (decimal)look.ModuleFadeSeconds;
        textFade.Value = (decimal)look.TextFadeSeconds;
        textWakes.SelectedIndex = look.TextWakesOnPointer ? 0 : 1;
    }

    public void Apply(OutputSettings now) => live.Look = now.Live;

    /// <summary>Writes what the controls hold into <paramref name="into"/>, keeping what an emptied box held.</summary>
    public void Read(OutputSettings into)
    {
        var was = into.Live;

        into.Live = new LivePatchingLook
        {
            ModuleOpacity = moduleOpacity.Value is { } module ? (int)Math.Round(module) : was.ModuleOpacity,
            TextOpacity = textOpacity.Value is { } band ? (int)Math.Round(band) : was.TextOpacity,
            ModuleFadeSeconds = moduleFade.Value is { } modules ? (double)modules : was.ModuleFadeSeconds,
            TextFadeSeconds = textFade.Value is { } text ? (double)text : was.TextFadeSeconds,
            TextWakesOnPointer = textWakes.SelectedIndex != 1,
        };

        into.Live.Clamp();
    }

    private static NumericUpDown Number(string name, double least, double most, double step) => new()
    {
        Name = name,
        Minimum = (decimal)least,
        Maximum = (decimal)most,
        Increment = (decimal)step,
        FormatString = "0.#",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
}

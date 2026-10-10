using Flyback.Editor.Bars;
using Flyback.Editor.Notices;
using Flyback.Ui.Midi;

namespace Flyback.Editor.Keys;

/// <summary>
/// The keys on the screen: up while a MIDI In listens to the computer keyboard, brought
/// up by a finger or the toolbar's keys button, which is on the bar only then, and laid
/// out as the keyboard is, a piano or the patch's scale.
/// </summary>
/// <remarks>
/// A finger asks for the keys once, the first time one presses anything; from then on,
/// and on a desktop from the start, the keys button decides. The row is put away whenever
/// nothing listens, and comes back on the next patch that does.
/// </remarks>
internal sealed class ScreenKeys : IReactTo<PatchCompiled>, IReactTo<KeysAsked>
{
    /// <summary>The bottom row from Z to the comma: an octave of a piano, C to the C above.</summary>
    private static readonly string[] PianoRow =
        ["KeyZ", "KeyS", "KeyX", "KeyD", "KeyC", "KeyV", "KeyG", "KeyB", "KeyH", "KeyN", "KeyJ", "KeyM", "Comma"];

    /// <summary>The home row, which a scale runs up from its tonic.</summary>
    private static readonly string[] ScaleRow =
        ["KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK", "KeyL", "Semicolon", "Quote", "Backslash"];

    /// <summary>What is printed on the keys whose browser name is not a letter.</summary>
    private static readonly Dictionary<string, string> Letters = new(StringComparer.Ordinal)
    {
        ["Comma"] = ",",
        ["Semicolon"] = ";",
        ["Quote"] = "'",
        ["Backslash"] = "\\",
    };

    private readonly MidiHub midi;
    private readonly Playback playback;
    private readonly Toolbar toolbar;
    private readonly LastPress lastPress;

    /// <summary>Whether a running program reads the computer keyboard, so the keys have something to play.</summary>
    private bool keyed;

    /// <summary>What the keys button was last pressed to, or null while nobody has pressed it.</summary>
    private bool? asked;

    private bool away;

    /// <summary>The row itself.</summary>
    public Keybed View { get; } = new() { IsVisible = false };

    public ScreenKeys(MidiHub midi, Playback playback, Toolbar toolbar, LastPress lastPress, ReportLine report)
    {
        this.midi = midi;
        this.playback = playback;
        this.toolbar = toolbar;
        this.lastPress = lastPress;

        View.Struck += code => midi.KeyDown(code);
        View.Released += code => midi.KeyUp(code);
        View.Shifted += octaves =>
        {
            report.Say(midi.Shift(octaves));
            Lay();
        };

        lastPress.Changed += Refresh;

        toolbar.Keys.IsVisible = false;
    }

    /// <summary>Whether the picture has the window, where the keys are put away.</summary>
    public bool Away
    {
        get => away;
        set
        {
            away = value;
            Refresh();
        }
    }

    /// <summary>Whether the keys are up.</summary>
    public bool Shown => View.IsVisible;

    public Task On(PatchCompiled notice)
    {
        keyed = playback.Keyed;
        Lay();
        Refresh();

        return Task.CompletedTask;
    }

    public Task On(KeysAsked notice)
    {
        // The button echoes what is shown; only a press that differs is somebody asking.
        if (notice.Shown != View.IsVisible) asked = notice.Shown;

        Refresh();

        return Task.CompletedTask;
    }

    /// <summary>Lays the row out as the keyboard is now: its scale, and its octave.</summary>
    private void Lay()
    {
        var keyboard = midi.Keyboard;
        var piano = keyboard.Scale is null;

        View.Lay(
        [
            .. (piano ? PianoRow : ScaleRow)
                .Select(code => (Code: code, Note: keyboard.Note(code)))
                .Where(key => key.Note is not null)
                .Select(key => new ScreenKey(
                    key.Code,
                    key.Note!.Value,
                    piano && key.Note.Value % 12 is 1 or 3 or 6 or 8 or 10,
                    Letters.GetValueOrDefault(key.Code) ?? key.Code.Replace("Key", string.Empty, StringComparison.Ordinal))),
        ]);
    }

    private void Refresh()
    {
        if (asked is null && lastPress.ByFinger) asked = true;

        var shown = keyed && asked == true && !away;

        if (!shown) View.Release();

        View.IsVisible = shown;

        // On the bar only while there is something to play, so a patch with nothing to play keeps the bar it had.
        if (toolbar.Keys.IsVisible != keyed)
        {
            toolbar.Keys.IsVisible = keyed;
            toolbar.Overflow.Fit(toolbar.View.Bounds.Width);
        }

        if (toolbar.Keys.IsChecked != shown) toolbar.Keys.IsChecked = shown;
    }
}

using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Notices;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The input whose well was clicked, which then takes keys: arrows nudge it, Home and
/// End send it to the ends of its range, Backspace resets it, Tab moves to the next
/// well on the module, Enter opens it for an exact value to be typed, and Alt+L
/// drops the list of panel knobs to link it to.
/// </summary>
/// <remarks>
/// No bare key starts the typing, so the computer keyboard still plays notes. What is
/// typed is drawn on the readout rather than in a text box, and the patch follows
/// every keystroke that reads as a value.
/// </remarks>
internal sealed class SocketFocus(SocketDial dial, KnobChoice choice, CanvasHistory history, Repaint repaint, Reactions reactions)
{
    /// <summary>How far one nudge moves a value along its travel, and the fine and coarse ones.</summary>
    private const double Nudge = 0.01, FineNudge = 0.002, CoarseNudge = 0.1;

    private SocketPick? picked;

    /// <summary>The value when the typing began, which Escape puts back.</summary>
    private float typingFrom;

    /// <summary>The input the keys go to, if it is still one that can be turned.</summary>
    public SocketPick? Focused => picked is { } pick && dial.Dialable(pick.Node, pick.Port, out _, out _) ? pick : null;

    /// <summary>What has been typed so far, or null while nothing is being typed.</summary>
    public string? Typed { get; private set; }

    /// <summary>How the well of <paramref name="port"/> on <paramref name="node"/> is drawn now.</summary>
    public WellState StateOf(Guid node, int port)
    {
        var pick = new SocketPick(node, port);

        if (dial.Dialed == pick) return dial.Fine ? WellState.Fine : WellState.Turn;
        if (dial.Turning) return WellState.Rest;
        if (Focused == pick) return Typed is null ? WellState.Focus : WellState.Edit;

        return dial.Hovered == pick ? WellState.Hover : WellState.Rest;
    }

    /// <summary>What the readout shows: the turn under way, or else the input with the focus.</summary>
    public (SocketPick Pick, TurnReadout.Turn Turn)? Reading
    {
        get
        {
            if (dial.Reading is { } turning) return turning;

            if (Focused is not { } pick
                || !dial.Dialable(pick.Node, pick.Port, out var node, out var spec)
                || NodeCatalog.Get(node.TypeId) is not { } def)
                return null;

            var value = node.InputValues[pick.Port];

            return (pick, new TurnReadout.Turn(
                spec.Name,
                node.Title(def),
                spec,
                value,
                Typed is null ? value : typingFrom,
                Math.Min(spec.Min, value),
                Math.Max(spec.Max, value),
                Typed is null ? WellState.Focus : WellState.Edit,
                Typed));
        }
    }

    /// <summary>Gives the keys to one input.</summary>
    public void Pick(SocketPick pick)
    {
        if (picked == pick) return;

        Clear();
        picked = pick;
        repaint.Request();
    }

    /// <summary>Takes the keys back, keeping whatever was typed that read as a value.</summary>
    public void Clear()
    {
        if (picked is null) return;

        if (Typed is not null) Commit(evenUnread: true);

        picked = null;
        repaint.Request();
    }

    /// <summary>Opens the input with the focus for a value to be typed.</summary>
    public bool BeginTyping()
    {
        if (Focused is not { } pick || !dial.Dialable(pick.Node, pick.Port, out var node, out _)) return false;

        typingFrom = node.InputValues[pick.Port];
        Typed = string.Empty;
        repaint.Request();
        return true;
    }

    /// <summary>Adds what was typed, and sets the value at once where it reads as one.</summary>
    public void Type(string text)
    {
        if (Typed is null) return;

        Typed += text;
        Follow();
    }

    /// <summary>Answers a key while an input has the focus, and says whether it was its.</summary>
    public bool Key(Key key, KeyModifiers modifiers)
    {
        if (choice.Key(key, modifiers)) return true;

        if (Focused is null || dial.Turning) return false;

        var command = (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        if (Typed is not null) return !command && TypingKey(key, modifiers);

        if (command) return false;

        if (key == Avalonia.Input.Key.L && (modifiers & KeyModifiers.Alt) != 0 && Focused is { } linking)
        {
            choice.Open(linking);
            return true;
        }

        var shift = (modifiers & KeyModifiers.Shift) != 0;

        switch (key)
        {
            case Avalonia.Input.Key.Up: return Move(shift ? FineNudge : Nudge, 1);
            case Avalonia.Input.Key.Down: return Move(shift ? FineNudge : Nudge, -1);
            case Avalonia.Input.Key.PageUp: return Move(CoarseNudge, 1);
            case Avalonia.Input.Key.PageDown: return Move(CoarseNudge, -1);
            case Avalonia.Input.Key.Home: return End(top: false);
            case Avalonia.Input.Key.End: return End(top: true);
            case Avalonia.Input.Key.Enter: return BeginTyping();
            case Avalonia.Input.Key.Back: return Focused is { } pick && dial.Reset(pick.Node, pick.Port);
            case Avalonia.Input.Key.Tab: return Next(shift ? -1 : 1);

            case Avalonia.Input.Key.Escape:
                Clear();
                return true;

            default: return false;
        }
    }

    /// <summary>Nudges the input with the focus by notches of the wheel.</summary>
    public bool Wheel(double notches) => Move(Nudge, Math.Sign(notches));

    /// <summary>The keys while typing: every key is the box's, so none of them plays a note.</summary>
    private bool TypingKey(Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Avalonia.Input.Key.Enter:
                Commit();
                return true;

            case Avalonia.Input.Key.Escape:
                Cancel();
                return true;

            case Avalonia.Input.Key.Tab:
                Commit();
                if (Typed is null && Next((modifiers & KeyModifiers.Shift) != 0 ? -1 : 1)) BeginTyping();
                return true;

            case Avalonia.Input.Key.Back:
                if (Typed is { Length: > 0 } typed) Typed = typed[..^1];
                Follow();
                return true;

            default:
                return true;
        }
    }

    /// <summary>Sets the value to what is typed, where it reads as one.</summary>
    private void Follow()
    {
        repaint.Request();

        if (Focused is not { } pick || !dial.Dialable(pick.Node, pick.Port, out _, out var spec)) return;

        if (spec.Read(Typed ?? string.Empty) is { } value) dial.Set(pick.Node, pick.Port, value);
    }

    /// <summary>
    /// Closes the box on the value it reads as. Text that reads as nothing keeps it open,
    /// unless <paramref name="evenUnread"/>, when the last value that did read stays.
    /// </summary>
    private void Commit(bool evenUnread = false)
    {
        if (Focused is not { } pick || !dial.Dialable(pick.Node, pick.Port, out _, out var spec))
        {
            Typed = null;
            return;
        }

        if (!evenUnread && spec.Read(Typed ?? string.Empty) is null) return;

        Typed = null;
        reactions.Raise(new InputLetGo(pick));
        repaint.Request();
    }

    /// <summary>Closes the box and puts back the value it opened on.</summary>
    private void Cancel()
    {
        if (Focused is { } pick) dial.Set(pick.Node, pick.Port, typingFrom);

        Typed = null;
        if (Focused is { } back) reactions.Raise(new InputLetGo(back));
        repaint.Request();
    }

    private bool Move(double by, int sign)
    {
        if (sign == 0 || Focused is not { } pick || !dial.Dialable(pick.Node, pick.Port, out var node, out var spec)) return false;

        var value = node.InputValues[pick.Port];
        float next;

        if (spec.Stepped)
        {
            var steps = by >= CoarseNudge ? Math.Max(1f, MathF.Round((spec.Max - spec.Min) * (float)by)) : 1f;

            next = value + sign * steps;
        }
        else
        {
            var min = Math.Min(spec.Min, value);
            var max = Math.Max(spec.Max, value);

            next = spec.At(Math.Clamp(spec.Travel(value, min, max) + sign * by, 0, 1), min, max);
        }

        dial.Set(pick.Node, pick.Port, next);
        reactions.Raise(new InputLetGo(pick));
        repaint.Request();
        return true;
    }

    private bool End(bool top)
    {
        if (Focused is not { } pick || !dial.Dialable(pick.Node, pick.Port, out _, out var spec)) return false;

        dial.Set(pick.Node, pick.Port, top ? spec.Max : spec.Min);
        reactions.Raise(new InputLetGo(pick));
        return true;
    }

    /// <summary>Moves the focus to the next well on the same module, round to the first.</summary>
    private bool Next(int step)
    {
        if (Focused is not { } pick || history.Patch.Find(pick.Node) is not { } node || NodeCatalog.Get(node.TypeId) is not { } def) return false;

        var count = def.Inputs.Count;

        for (var i = 1; i < count; i++)
        {
            var port = ((pick.Port + step * i) % count + count) % count;

            if (!ValueWell.Has(history.Patch, node, def, port)) continue;

            picked = new SocketPick(pick.Node, port);
            repaint.Request();
            return true;
        }

        return true;
    }
}

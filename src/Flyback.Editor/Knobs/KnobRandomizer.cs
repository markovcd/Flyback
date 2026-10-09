using System.Diagnostics;
using Avalonia.Threading;
using Flyback.Editor.Canvas;
using Flyback.Ui.Midi;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Statistics;
using Flyback.Core.Graph;
using Flyback.Ui;
using Flyback.Host;

namespace Flyback.Editor.Knobs;

/// <summary>
/// Sends every panel knob that is not held somewhere new, gliding there, and back
/// again; fired from the panel's die, Ctrl+Shift+K or a learned controller button.
/// </summary>
/// <remarks>
/// A randomize turns the knobs the way a hand does, so like a hand it is not an
/// undo step (ADR-0086); <see cref="Back"/> is its own way back.
/// </remarks>
internal sealed class KnobRandomizer : IReactTo<DocumentArrived>
{
    /// <summary>How many randomizes back <see cref="Back"/> reaches.</summary>
    private const int Remembered = 32;

    private readonly PanelKnobs knobs;
    private readonly ControlHub hub;
    private readonly NodeEditor editor;
    private readonly TextWriteBack writeBack;
    private readonly OutputSettingRepository settings;
    private readonly EditorFolders folders;
    private readonly ReportLine report;
    private readonly Usage usage;

    /// <summary>Where every knob was before each randomize, the latest last.</summary>
    private readonly List<Dictionary<Guid, float>> before = [];

    /// <summary>How often a glide moves the knobs on.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(16);

    private static readonly Stopwatch Running = Stopwatch.StartNew();

    private readonly HashSet<Guid> moved = [];

    private KnobGlide? gliding;
    private TimeSpan started;

    /// <summary>Which glide a scheduled step belongs to, so a step left over from one that ended does nothing.</summary>
    private int glide;
    private readonly MidiLearn learning;

    public KnobRandomizer(
        PanelKnobs knobs,
        ControlHub hub,
        NodeEditor editor,
        TextWriteBack writeBack,
        OutputSettingRepository settings,
        EditorFolders folders,
        MidiHub midi,
        ReportLine report,
        Usage usage)
    {
        this.knobs = knobs;
        this.hub = hub;
        this.editor = editor;
        this.writeBack = writeBack;
        this.settings = settings;
        this.folders = folders;
        learning = new MidiLearn(midi, report);
        this.report = report;
        this.usage = usage;

        knobs.View.Roll.RollRequested += Roll;
        knobs.View.Roll.BackRequested += Back;
        knobs.View.Roll.LearnRequested += learn =>
        {
            if (learn) _ = LearnAsync();
            else Forget();
        };

        knobs.View.Roll.Tuned += (amount, glide) =>
        {
            Wanted.Amount = amount;
            Wanted.GlideSeconds = glide;
        };

        knobs.View.Roll.TuneEnded += Save;

        // A hand on a gliding knob takes it.
        knobs.View.Turning += (id, _) => gliding?.Drop(id);
        knobs.Stage.Turning += (id, _) => gliding?.Drop(id);
        hub.Turned += (id, _) => Dispatcher.UIThread.Post(() => gliding?.Drop(id));

        hub.Trigger = Wanted.Trigger;
        hub.Triggered += () => Dispatcher.UIThread.Post(Roll);
        knobs.ModesStopped += () => learning.Cancel();

        Show();
    }

    /// <summary>Where each randomize lands, seeded by a test that wants to know.</summary>
    public Random Random { get; set; } = Random.Shared;

    /// <summary>The time a glide is measured by, moved by hand in a test.</summary>
    public Func<TimeSpan> Now { get; set; } = () => Running.Elapsed;

    /// <summary>Runs something after a while, on the UI thread; by hand in a test.</summary>
    public Action<TimeSpan, Action> After { get; set; } = (delay, act) => DispatcherTimer.RunOnce(act, delay);

    /// <summary>Whether knobs are still on their way.</summary>
    public bool Gliding => gliding is not null;

    private RandomizeSettings Wanted => settings.Current.Randomize;

    /// <summary>Sends every knob not held somewhere within the amount of where it is.</summary>
    public void Roll()
    {
        var controls = editor.History.Patch.Controls ?? [];
        var free = controls.Where(control => !control.Held).ToList();

        if (free.Count == 0)
        {
            report.Say(controls.Count == 0
                ? "There are no knobs to randomize. Add one with + on the knob panel."
                : "Every knob is held, so randomizing has nothing to move.");
            return;
        }

        before.Add(controls.ToDictionary(control => control.Id, control => control.Value));
        if (before.Count > Remembered) before.RemoveAt(0);

        usage.Count(Used.Randomized);
        Glide(free.ToDictionary(control => control.Id, control => (control.Value, KnobRoll.Next(control.Value, Wanted.Amount, Random))));
        Show();
    }

    /// <summary>Takes the knobs back to where they were before the last randomize.</summary>
    public void Back()
    {
        if (before.Count == 0)
        {
            report.Say("There is no randomize to go back from.");
            return;
        }

        var was = before[^1];
        before.RemoveAt(before.Count - 1);

        Glide((editor.History.Patch.Controls ?? [])
            .Where(control => was.ContainsKey(control.Id))
            .ToDictionary(control => control.Id, control => (control.Value, was[control.Id])));
        Show();
    }

    public Task On(DocumentArrived notice)
    {
        glide++;
        gliding = null;
        moved.Clear();
        before.Clear();
        Show();
        return Task.CompletedTask;
    }

    private void Glide(Dictionary<Guid, (float From, float To)> moves)
    {
        Settle();

        gliding = new KnobGlide(moves, TimeSpan.FromSeconds(Wanted.GlideSeconds));
        moved.UnionWith(moves.Keys);
        started = Now();

        Step(glide);
    }

    private void Step(int which)
    {
        if (which != glide || gliding is not { } going) return;

        var elapsed = Now() - started;

        foreach (var (id, at) in going.At(elapsed).ToList()) knobs.Turn(id, at);

        if (going.Done(elapsed)) Settle();
        else After(Tick, () => Step(which));
    }

    /// <summary>Stops a glide where it has got to, and writes where the knobs rest.</summary>
    private void Settle()
    {
        glide++;
        gliding = null;

        foreach (var id in moved) writeBack.LetGoOfKnob(id);
        moved.Clear();
    }

    private async Task LearnAsync()
    {
        if (learning.Start("No MIDI device is plugged in, so there is no button to learn.") is not { } cancel) return;

        Show();
        report.Say("Press a button or a pad on your controller to randomize the knobs with. Esc to stop.");

        try
        {
            if (await hub.LearnAsync(cancel.Devices, cancel.Token, notes: true) is not { } pressed) return;

            // Any channel, so the button goes on randomizing whichever track the controller is on.
            // A pad's note still plays whatever listens to it; the trigger only hears it too.
            var binding = pressed with { Channel = 0 };

            Wanted.Trigger = binding;
            hub.Trigger = binding;
            Save();
            report.Say($"{Explain(binding)} randomizes the knobs.");
        }
        finally
        {
            cancel.End();
            Show();
        }
    }

    private void Forget()
    {
        if (Wanted.Trigger is not { } was) return;

        Wanted.Trigger = null;
        hub.Trigger = null;
        Save();
        Show();
        report.Say($"{Explain(was)} no longer randomizes the knobs.");
    }

    private string Explain(MidiBinding binding) => knobs.View.Explain?.Invoke(binding) ?? binding.Label;

    private void Show() =>
        knobs.View.ShowRoll(Wanted.Amount, Wanted.GlideSeconds, Wanted.Trigger, before.Count > 0, learning.Active);

    private void Save() => settings.Save();
}

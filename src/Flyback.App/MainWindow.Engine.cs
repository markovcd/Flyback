using System.Globalization;
using Avalonia.Controls;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// The join between the window and the instrument: opening a sound device,
/// turning an edited patch back into two programs, and saying what came of
/// either. Everything the status bar carries originates here.
/// </summary>
/// <remarks>
/// A patch is recompiled whole on every edit rather than incrementally, which is
/// what keeps this to one handler and no invalidation to get wrong. The device
/// comes from a plugin, so nothing in this file knows what a backend is called
/// or which platform it is for.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>The device that was opened, and what it came from — null when nothing could play.</summary>
    private sealed record AudioSetup(IAudioDevice Device, IAudioOutput? Output, string? Failure);

    /// <summary>
    /// Opens the best backend the plugins offered. A machine with no sound
    /// plugin, or one whose device refuses to open, gets silence and a disabled
    /// button — never a program that will not start.
    /// </summary>
    private static AudioSetup OpenAudio(PluginCatalog plugins)
    {
        if (plugins.PreferredAudioOutput is not { } output)
            return new AudioSetup(new SilentAudioDevice(), null, null);

        try
        {
            return new AudioSetup(output.Create(AudioFormat.Default), output, null);
        }
        catch (Exception ex)
        {
            return new AudioSetup(new SilentAudioDevice(), null, $"{output.Name} — {ex.Message}");
        }
    }

    /// <summary>
    /// What loaded and what did not. This is the only place a plugin failure is
    /// reported, so it says where the folder is even when it is empty.
    /// </summary>
    private string PluginSummary()
    {
        var lines = new List<string> { plugins.Plugins.Count == 0 ? "No plugins loaded." : "Loaded:" };

        lines.AddRange(plugins.Plugins.Select(p => $"    {p.Info.Name}  ({p.Info.Id})"));

        // Module providers are worth naming separately: they are what a saved
        // patch records, and what another machine would have to install.
        var providers = plugins.Modules.Providers.Where(p => p.Id != NodeCatalog.BuiltInProvider.Id).ToList();

        if (providers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Modules from:");
            lines.AddRange(providers.Select(p => $"    {p.Name}  ({p.Id})"));
        }

        if (sound.Failure is { } failure)
            lines.Add($"Could not open sound: {failure}");

        // Where a key would be kept, but never what it is. An assistant with no
        // store behind it still works; it just forgets between runs, and saying
        // so here is the difference between that and appearing to have saved one.
        if (plugins.Assistants.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(plugins.PreferredSecretStore is { } store
                ? $"Keys are kept by: {store.Name}"
                : "No secret store is installed, so a key lasts only as long as the window.");
        }

        lines.AddRange(plugins.Problems.Select(p => $"Problem: {p}"));

        lines.Add(string.Empty);
        lines.Add(PluginHost.DefaultDirectory);

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The chart the picture is rooted at: the selected module, when that is a
    /// Probe or a Scope and not something else.
    /// </summary>
    /// <remarks>
    /// Selection rather than a mode, because a chart is something you look at
    /// rather than something a patch is left in: clicking the module shows it
    /// and clicking away puts the picture back, and nothing about the patch or
    /// the file changes either way. It leaves the sound alone as well — the
    /// speakers root at the Output whatever the screen is doing, so a patch can
    /// be heard while a chart of one corner of it is being read.
    /// </remarks>
    private NodeInstance? Probed =>
        editor.SelectedNode is { } selected && NodeCatalog.IsChart(selected.TypeId) ? selected : null;

    /// <summary>Which probe the picture was last compiled for, or null for the patch itself.</summary>
    private Guid? showingProbe;

    /// <summary>
    /// Selecting a Probe is what puts its chart on the screen and selecting
    /// anything else is what takes it off again. No other selection changes the
    /// picture, so this recompiles only when that one does.
    /// </summary>
    private void ProbeSelectionChanged()
    {
        if (Probed?.Id != showingProbe) Recompile();
    }

    /// <summary>
    /// One patch, one program per sink. The audio program is compiled even when
    /// sound is off, so switching it on is instant and the status line can show
    /// what the ear would cost.
    /// </summary>
    private void Recompile()
    {
        var probe = Probed;
        showingProbe = probe?.Id;

        var result = probe is null
            ? editor.Patch.CompileForVideo(samples: samples)
            : editor.Patch.CompileForProbe(probe.Id, samples: samples);

        preview.Program = result.Program;
        audio.Update(editor.Patch, samples);

        // Both programs are new, so both of their blocks are, and whatever is
        // being held has to be written into them before the next frame or the
        // next buffer. Turning a knob while playing a note recompiles the patch,
        // and the note must not be cut off by the edit.
        preview.Live = new LiveValues(result.Program.LiveInputs);
        midi.Follow(preview.Live, audio.Live);

        // What the ear reaches is said too. Compiling backwards from one sink
        // means the video pass never visits a module only the speakers reach —
        // and stops at the first line when there is no screen at all — so a
        // patch built for sound had nothing said about it, however wrong it was.
        var said = result.Issues
            .Concat(editor.Patch.CompileForAudio(samples: samples).Issues)
            .Select(i => i.Message)
            .Distinct();

        // That the screen is showing a chart rather than the patch is said here
        // and nowhere else. Without it a probe left selected looks exactly like
        // a patch that has stopped working.
        if (probe is not null)
        {
            said = said.Prepend(probe.TypeId == NodeCatalog.ScopeTypeId
                ? "Showing the Scope — it charts what the speakers played, so switch sound on "
                  + "to see anything. Select another module for the picture."
                : "Showing the Probe — select another module for the picture.");
        }

        // Each of them, rather than one sentence with bullets between: they are
        // separate problems, they arrive and are fixed separately, and the log
        // behind the line gives each its own row. The bar joins them back up,
        // because there is only one line to say them on.
        Report(said.ToList());

        MarkExportable();
    }

    /// <summary>
    /// The one place anything is said to the user. <paramref name="detail"/> is
    /// for what will not fit on a status bar — a list of missing plugins, say.
    /// </summary>
    /// <param name="progress">
    /// That this is the last message again with a new number in it, so the log
    /// behind the line keeps one entry for the run rather than one per update.
    /// </param>
    /// <remarks>
    /// The line itself, and what becomes of what it used to say, are
    /// <see cref="ReportLine"/>'s business — this stays the one door into it.
    /// </remarks>
    private void Report(string message, string? detail = null, bool progress = false) =>
        report.Say(message, detail, progress);

    /// <summary>
    /// The same, for everything a compile found at once. Each is a line of its
    /// own in the log; the bar joins them, having only the one line.
    /// </summary>
    private void Report(IReadOnlyList<string> messages) => report.Say(messages);

    private void SetAudioEnabled(bool enabled)
    {
        audioButton.Content = enabled ? "Audio on" : "Audio off";

        if (enabled)
        {
            audio.Update(editor.Patch);

            try
            {
                audio.Start();
            }
            catch (Exception ex)
            {
                // A device is only really opened here, so this is where a card
                // that is busy, unplugged or missing its library says so. The
                // button goes back off and stays off: whatever it is will not
                // have fixed itself by the next click, and ADR-0025 promises
                // that nothing a plugin does takes the shell down.
                Report($"Sound could not start — {ex.Message}", PluginSummary());
                audioButton.IsEnabled = false;
                audioButton.IsChecked = false;
                return;
            }

            // Sound cannot stretch, so it leads and the picture follows — and
            // the same tick is where a Scope's chart is refilled from what the
            // speakers have just played. Here rather than in the renderer
            // because this is the one moment in the loop when the two paths are
            // both stopped: the callback is not mid-buffer as far as anything
            // here can tell, and the frame has not started. It is also the exact
            // scope of the promise the module makes — no clock, no sound, and
            // nothing new to chart.
            preview.Clock = () =>
            {
                audio.RefreshTraces(preview.Program);
                return audio.Time;
            };
        }
        else
        {
            preview.Clock = null;
            audio.Stop();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Before the device goes, and before anything else: a take whose header
        // was never patched is not a file, so closing the window mid-recording
        // has to finish it rather than abandon it.
        Stop();

        audio.Dispose();

        // And the instruments, which are hardware somebody else may want back. A
        // port left open outlives the window that was reading it.
        midi.Dispose();

        base.OnClosed(e);
    }

    private void UpdateStatus()
    {
        var nodes = editor.Patch.Nodes.Count;
        var wires = editor.Patch.Connections.Count;
        var ops = preview.Program.Ops.Length;
        var ms = preview.FrameMilliseconds;
        var size = preview.Resolution;

        // The loop is capped at ~60 Hz, so report the cost of a frame rather
        // than a frame rate the timer would never let you observe. Which renderer
        // produced the number is part of what it means, so it is said alongside.
        status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{nodes} modules · {wires} wires · {ops} ops   |   t = {preview.Time:0.00}s   |   {ms:0.0} ms to render {size.Width} × {size.Height} on the {preview.BackendName}");
    }
}

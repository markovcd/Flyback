using System.Globalization;
using Avalonia.Controls;
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
    /// One patch, one program per sink. The audio program is compiled even when
    /// sound is off, so switching it on is instant and the status line can show
    /// what the ear would cost.
    /// </summary>
    private void Recompile()
    {
        var result = editor.Patch.CompileForVideo();
        preview.Program = result.Program;
        audio.Update(editor.Patch);

        // What the ear reaches is said too. Compiling backwards from one sink
        // means the video pass never visits a module only the speakers reach —
        // and stops at the first line when there is no screen at all — so a
        // patch built for sound had nothing said about it, however wrong it was.
        var said = result.Issues
            .Concat(editor.Patch.CompileForAudio().Issues)
            .Select(i => i.Message)
            .Distinct()
            .ToArray();

        Report(said.Length > 0 ? string.Join("  •  ", said) : string.Empty);

        MarkExportable();
    }

    /// <summary>
    /// The one place anything is said to the user. <paramref name="detail"/> is
    /// for what will not fit on a status bar — a list of missing plugins, say —
    /// and is cleared along with the line, so nothing stale hangs off it.
    /// </summary>
    private void Report(string message, string? detail = null)
    {
        issues.Text = message;
        ToolTip.SetTip(issues, string.IsNullOrEmpty(detail) ? null : detail);
    }

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

            // Sound cannot stretch, so it leads and the picture follows.
            preview.Clock = () => audio.Time;
        }
        else
        {
            preview.Clock = null;
            audio.Stop();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        audio.Dispose();
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

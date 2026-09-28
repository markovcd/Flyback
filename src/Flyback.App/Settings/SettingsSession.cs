using Flyback.App.Assist;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Knobs;
using Flyback.App.Notices;
using Flyback.App.Statistics;
using Flyback.App.Updates;

namespace Flyback.App.Settings;

/// <summary>Shows the settings sections together and commits or restores their drafts.</summary>
internal sealed class SettingsSession(
    AssistantPanel assistant,
    OutputSections output,
    CanvasSection canvas,
    UpdatesSection updates,
    UsageSection usageSection,
    FilesSection files,
    PanelKnobs knobs,
    IDialog dialog,
    Usage usage,
    OutputSettingsUse outputSettings)
    : IReactTo<SettingsAsked>
{
    /// <summary>Whether the settings sheet is waiting for an answer.</summary>
    public bool IsShowing { get; private set; }

    public Task On(SettingsAsked notice) => ShowAsync();

    /// <summary>Shows all sections, then saves their drafts or restores the last saved values.</summary>
    public async Task ShowAsync()
    {
        _ = output.ShowFfmpegAsync();
        output.ShowMonitors();

        IsShowing = true;
        usage.Count(Used.Settings);

        bool saved;

        try
        {
            saved = await SettingsDialog.ShowAsync(dialog,
            [
                ("Graphics", output.Graphics),
                ("Canvas", canvas.View),
                ("Recording", output.Recording),
                ("Sound", output.Sound),
                ("MIDI", knobs.MidiSection),
                ("Assistant", assistant.SettingsSection()),
                ("Files", files.View),
                ("Updates", updates.View),
                ("Usage", usageSection.View),
            ]);
        }
        finally
        {
            IsShowing = false;
        }

        if (saved)
        {
            assistant.SaveSettings();
            outputSettings.Save();
            updates.Save();
            usageSection.Save();
            canvas.Save();
            files.Save();
            return;
        }

        assistant.DiscardSettings();
        output.Show();
        updates.Show();
        usageSection.Show();
        canvas.Show();
        files.Show();
    }
}

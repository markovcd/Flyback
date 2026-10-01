using Flyback.App.Assist;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Notices;
using Flyback.App.Statistics;

namespace Flyback.App.Settings;

/// <summary>Shows the settings sections together and commits or restores their drafts.</summary>
internal sealed class SettingsSession(
    PictureSection picture,
    SoundSection sound,
    MidiSection midi,
    RecordingSection recording,
    CanvasSection canvas,
    FilesSection files,
    AssistantSection assistant,
    PrivacySection privacy,
    IDialog dialog,
    Usage usage,
    OutputSettingsUse outputSettings)
    : IReactTo<SettingsAsked>
{
    /// <summary>The tabs, in the order the window lists them.</summary>
    private readonly ISettingsSection[] sections = [picture, sound, midi, recording, canvas, files, assistant, privacy];

    /// <summary>Whether the settings sheet is waiting for an answer.</summary>
    public bool IsShowing { get; private set; }

    public Task On(SettingsAsked notice) => ShowAsync();

    /// <summary>Shows all sections, then saves their drafts or restores the last saved values.</summary>
    public async Task ShowAsync()
    {
        foreach (var section in sections) section.Opening();

        IsShowing = true;
        usage.Count(Used.Settings);

        bool saved;

        try
        {
            saved = await SettingsDialog.ShowAsync(dialog, [.. sections.Select(s => (s.Name, s.View))]);
        }
        finally
        {
            IsShowing = false;
        }

        if (saved)
        {
            outputSettings.Save();
            foreach (var section in sections) section.Save();
            return;
        }

        outputSettings.Show();
        foreach (var section in sections) section.Show();
    }
}

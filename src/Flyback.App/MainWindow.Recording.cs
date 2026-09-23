using Avalonia.Platform.Storage;

namespace Flyback.App;

/// <summary>
/// The window's side of a take: which button press means what, and where the file
/// goes. The take itself is <see cref="TakeRecording"/>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The take this window is recording, counting in, or about to.</summary>
    internal TakeRecording Recording { get; }

    /// <summary>
    /// What the toolbar button's press means: start a take, call off the count
    /// before one, or end the one running. The same control does all three — a
    /// take has no length, so stopping it is the only way it ever finishes.
    /// </summary>
    private async Task ToggleRecordAsync()
    {
        if (!recordButton.IsEnabled) return;

        if (Recording.Counting)
        {
            Recording.CallOffCount();
            return;
        }

        if (Recording.Running)
        {
            Recording.Stop();
            return;
        }

        await RecordAsync();
    }

    /// <summary>Asks where the take goes, counts it in, and starts it.</summary>
    private async Task RecordAsync()
    {
        var kinds = Recording.Kinds();

        if (kinds.Count == 0)
        {
            Report(TakeRecording.NothingToRecord);
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Record",
            FileTypeChoices = kinds,
            SuggestedFileName = Takes.FileNameFor(patchName),
            DefaultExtension = kinds[0].Patterns?[0].TrimStart('*', '.'),
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        // A take is of a patch that is playing, and a paused one has no sound to record.
        Resume();

        // Before the count rather than only as the file is opened: a count-in is
        // three seconds of standing ready, and spending them to be told there is
        // no ffmpeg is three seconds nobody gets back.
        if (Recording.Refusal(path) is { } refused)
        {
            Report(refused);
            return;
        }

        await Recording.CountInAsync(path, TakeRecording.CountInStep);
    }
}

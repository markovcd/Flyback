using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.App.Files;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The Files tab of the settings window: which program opens Flyback's files, told
/// to the operating system on Save and on nothing else (ADR-0127).
/// </summary>
public sealed class FileTypeSettingsTests : UiTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-file-types-" + Guid.NewGuid().ToString("N"),
        "file-types.json");

    private readonly Recorded system = new();

    private const int FilesTab = 6;

    public override void Dispose()
    {
        base.Dispose();

        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private sealed class Recorded : FileTypes
    {
        public List<FileOpener> Applied { get; } = [];

        public override void Apply(FileOpener opener) => Applied.Add(opener);
    }

    private MainWindow Open()
    {
        var window = Owned(new MainWindow(fileTypeSettingsPath: settingsPath, fileTypes: system));

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ModalOverlay OpenSettings(MainWindow window)
    {
        All<Button>(window).Single(b => b.Name == "settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        var dialog = All<ModalOverlay>(window).Single();

        All<TabControl>(dialog).Single(t => t.Name == "settingsTabs").SelectedIndex = FilesTab;
        Settle(window);

        return dialog;
    }

    private static void Close(MainWindow window, ModalOverlay dialog, string by)
    {
        All<Button>(dialog)
            .Single(b => b.Content as string == by)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    private static ComboBox Opener(ModalOverlay dialog) =>
        All<ComboBox>(dialog).Single(c => c.Name == "fileOpener");

    [AvaloniaFact]
    public void Flyback_claims_no_files_until_asked()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        Opener(dialog).SelectedIndex.ShouldBe((int)FileOpener.None);

        Close(window, dialog, "Save");

        system.Applied.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Saving_the_viewer_tells_the_system_and_is_kept()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        Opener(dialog).SelectedIndex = (int)FileOpener.Viewer;
        Close(window, dialog, "Save");

        system.Applied.ShouldBe([FileOpener.Viewer]);
        FileTypeSettings.Load(settingsPath).Opener.ShouldBe(FileOpener.Viewer);
    }

    [AvaloniaFact]
    public void Cancel_tells_the_system_nothing_and_puts_the_choice_back()
    {
        var window = Open();
        var dialog = OpenSettings(window);

        Opener(dialog).SelectedIndex = (int)FileOpener.Editor;
        Close(window, dialog, "Cancel");

        system.Applied.ShouldBeEmpty();
        Opener(OpenSettings(window)).SelectedIndex.ShouldBe((int)FileOpener.None);
    }

    [AvaloniaFact]
    public void Choosing_nothing_after_the_editor_takes_the_files_back_once()
    {
        new FileTypeSettings { Opener = FileOpener.Editor }.Save(settingsPath);

        var window = Open();
        var dialog = OpenSettings(window);

        Opener(dialog).SelectedIndex = (int)FileOpener.None;
        Close(window, dialog, "Save");
        Close(window, OpenSettings(window), "Save");

        system.Applied.ShouldBe([FileOpener.None]);
    }

    /// <summary>A copy that was moved points the files at where it is now.</summary>
    [AvaloniaFact]
    public void Every_save_registers_the_chosen_program_again()
    {
        new FileTypeSettings { Opener = FileOpener.Editor }.Save(settingsPath);

        var window = Open();

        Close(window, OpenSettings(window), "Save");
        Close(window, OpenSettings(window), "Save");

        system.Applied.ShouldBe([FileOpener.Editor, FileOpener.Editor]);
    }
}

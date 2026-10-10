using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Editor.Controls;
using Flyback.Editor.Files;
using Flyback.Editor.Windows;
using Flyback.Ui;
using Shouldly;
using Flyback.Host;

namespace Flyback.Editor.Tests.Files;

/// <summary>
/// The Files tab of the settings window: which program opens Flyback's files, told
/// to the operating system on Save and on nothing else (ADR-0127).
/// </summary>
public sealed class FileTypeSettingsTests : EditorTest
{
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-file-types-" + Guid.NewGuid().ToString("N"),
        "settings.json");

    private readonly Recorded system = new();

    private const string FilesTab = "Files";

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
        var window = NewMainWindow(new EditorSetup
        {
            Folders = new() { SettingsPath = settingsPath },
            Host = new() { FileTypes = system },
        });

        window.Show();
        Settle(window);
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ComboBox Opener(ModalOverlay dialog) =>
        All<ComboBox>(dialog).Single(c => c.Name == "fileOpener");

    [AvaloniaFact]
    public void Flyback_claims_no_files_until_asked()
    {
        var window = Open();
        var dialog = OpenSettings(window, FilesTab);

        Opener(dialog).SelectedIndex.ShouldBe((int)FileOpener.None);

        CloseSettings(window, dialog);

        system.Applied.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Saving_the_viewer_tells_the_system_and_is_kept()
    {
        var window = Open();
        var dialog = OpenSettings(window, FilesTab);

        Opener(dialog).SelectedIndex = (int)FileOpener.Viewer;
        CloseSettings(window, dialog);

        system.Applied.ShouldBe([FileOpener.Viewer]);
        FileTypeSettings.Load(settingsPath).Opener.ShouldBe(FileOpener.Viewer);
    }

    [AvaloniaFact]
    public void Cancel_tells_the_system_nothing_and_puts_the_choice_back()
    {
        var window = Open();
        var dialog = OpenSettings(window, FilesTab);

        Opener(dialog).SelectedIndex = (int)FileOpener.Editor;
        CloseSettings(window, dialog, "cancel");

        system.Applied.ShouldBeEmpty();
        Opener(OpenSettings(window, FilesTab)).SelectedIndex.ShouldBe((int)FileOpener.None);
    }

    [AvaloniaFact]
    public void Choosing_nothing_after_the_editor_takes_the_files_back_once()
    {
        new FileTypeSettings { Opener = FileOpener.Editor }.Save(settingsPath);

        var window = Open();
        var dialog = OpenSettings(window, FilesTab);

        Opener(dialog).SelectedIndex = (int)FileOpener.None;
        CloseSettings(window, dialog);
        CloseSettings(window, OpenSettings(window, FilesTab));

        system.Applied.ShouldBe([FileOpener.None]);
    }

    /// <summary>A copy that was moved points the files at where it is now.</summary>
    [AvaloniaFact]
    public void Every_save_registers_the_chosen_program_again()
    {
        new FileTypeSettings { Opener = FileOpener.Editor }.Save(settingsPath);

        var window = Open();

        CloseSettings(window, OpenSettings(window, FilesTab));
        CloseSettings(window, OpenSettings(window, FilesTab));

        system.Applied.ShouldBe([FileOpener.Editor, FileOpener.Editor]);
    }

    [AvaloniaFact]
    public void The_library_folder_is_kept_with_the_output_settings()
    {
        var window = Open();
        var dialog = OpenSettings(window, FilesTab);

        All<TextBox>(dialog).Single(t => t.Name == "library").Text = @"  D:\Sounds  ";
        CloseSettings(window, dialog);

        OutputSettings.Load(settingsPath).Library.ShouldBe(@"D:\Sounds");
    }

    [AvaloniaFact]
    public void A_saved_library_folder_is_looked_in_from_launch()
    {
        new OutputSettings { Library = @"D:\Sounds" }.Save(settingsPath);

        var window = Open();

        Service<PatchFiles>(window).SoundFolder.Library.ShouldBe(@"D:\Sounds");
        Service<PatchFiles>(window).PictureFolder.Library.ShouldBe(@"D:\Sounds");
    }
}

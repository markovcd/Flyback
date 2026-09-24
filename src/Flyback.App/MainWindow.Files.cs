using Avalonia.Input;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// The routes into and out of <see cref="PatchFiles"/>: the Open and Save gestures,
/// a drop, an activation and a path named on the command line, each asking first
/// whatever has to be asked.
/// </summary>
public sealed partial class MainWindow
{
    /// <inheritdoc cref="PatchFiles.Became"/>
    internal void Became(string? name, string? beside, BundleFiles? files = null) => this.files.Became(name, beside, files);

    /// <summary>
    /// Whether the document is a bundle, which is what the next save offers first.
    /// Readable from the tests, as <see cref="Became"/> is callable from them:
    /// every route that opens a document is behind a file picker the headless
    /// platform does not put up.
    /// </summary>
    internal bool IsBundle => files.IsBundle;

    /// <summary>Puts a patch that has just been read on the canvas, from its beginning.</summary>
    private void Show(Patch patch)
    {
        ClearPresetSelection();

        editor.Patch = patch;
        RewindToZero();
    }

    /// <summary>
    /// The Open gesture whole: what is unsaved is asked about, and then the
    /// picker. The toolbar's button and Ctrl+O both come through here, so the
    /// question cannot be stepped round by reaching for the keyboard.
    /// </summary>
    private async Task OpenAnotherPatchAsync()
    {
        if (await MayReplaceThePatchAsync() && await files.PickOpenAsync() is { } file) await OpenFileAsync(file);
    }

    /// <summary>
    /// Opens a file handed back by any of the routes that produce one — a
    /// picker, a drop from the file explorer, a path named on the command
    /// line, or a file macOS hands the program through an activation — so the
    /// extension decides which kind it is exactly as it does for the picker.
    /// </summary>
    private async Task OpenFileAsync(IStorageFile file)
    {
        if (PluginPackage.Named(file.Name)) await pluginInstalls.InstallAsync(file);
        else await files.OpenFileAsync(file);
    }

    /// <summary>
    /// Opens a file already sitting on disk rather than one a picker handed
    /// back — named on the command line when the program started, or resolved
    /// from a plain path some other way.
    /// </summary>
    private async Task OpenPathAsync(string path)
    {
        IStorageFile? file;

        try
        {
            file = await StorageProvider.TryGetFileFromPathAsync(path);
        }
        catch (Exception ex)
        {
            Report($"Could not open {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        if (file is null)
        {
            Report($"Could not open {Path.GetFileName(path)}.");
            return;
        }

        await OpenFileAsync(file);
    }

    /// <summary>
    /// Lets a patch, a bundle or a text file be opened by dropping it in from
    /// the file explorer — the same three kinds the picker offers, arriving
    /// without one.
    /// </summary>
    private void WireFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);

        // Refused under a dialog, and shown as refused, for the reason
        // OpenActivatedFileAsync gives.
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !this.HasDialogUp
                ? DragDropEffects.Copy
                : DragDropEffects.None);

        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            // Only the first: one window holds one patch, and a picker never
            // offers more than that either.
            if (e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is not { } file) return;

            e.Handled = true;

            await OpenActivatedFileAsync(file);
        });
    }

    /// <summary>
    /// Opens a file handed to the program from outside a picker or a drop —
    /// which on macOS is how "open this file" arrives at all: Finder delivers
    /// it as an activation rather than as a command-line argument, whether
    /// that launches the program or lands on its Dock icon while it is
    /// already running. See <see cref="FlybackApp.OnFrameworkInitializationCompleted"/>.
    /// </summary>
    /// <remarks>
    /// Not while a dialog is up. A dialog stops the pointer and the keyboard and
    /// neither of these arrives by them, so with nothing unsaved to ask about the
    /// document would be replaced behind the sheet — and a text one takes the
    /// focus with it, out of a dialog that then no longer hears Escape.
    /// </remarks>
    internal async Task OpenActivatedFileAsync(IStorageFile file)
    {
        if (this.HasDialogUp)
        {
            Report($"{file.Name} was not opened: there is a dialog to answer first.");
            return;
        }

        if (PluginPackage.Named(file.Name) || await MayReplaceThePatchAsync()) await OpenFileAsync(file);
    }

    /// <returns>Whether the document was saved. A canceled picker is not a save, and nor is a copy.</returns>
    private async Task<bool> SavePatchAsync() =>
        await files.PickSaveAsync() is { } file && await SaveToAsync(file);

    /// <summary>Writes the document to a file the picker handed back, as the kind its name says.</summary>
    /// <returns>
    /// Whether the document was saved — which writing a file is, except where the
    /// file is a printing of a patch the graph owns: that is a copy, and leaves
    /// whatever was unsaved as unsaved as it was.
    /// </returns>
    internal async Task<bool> SaveToAsync(IStorageFile file)
    {
        // A copy saves nothing, so whatever asked for a save has not had one: the
        // unsaved-changes question reads this, and going ahead on the strength of
        // a printing would shut the only whole patch there is.
        if (PatchFileKinds.Sourced(file.Name)) return await files.SaveSourceAsync(file) && !SomethingToLose;

        // Either of the other two hands the patch to the graph and empties the
        // text, so text that is nowhere else is asked about first — ADR-0068.
        return await MayLoseTheTextToAsync(file.Name) && await files.SavePatchFileAsync(file);
    }
}

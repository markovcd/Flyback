using Flyback.Core;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// One kept group, as it was read back off the disk.
/// </summary>
/// <remarks>
/// The whole of what was read, trouble included: a fragment naming a module this
/// build has not got is still listed, because the entry is a real thing somebody
/// saved and hiding it would leave them wondering where it went. What it cannot
/// do is arrive quietly full of holes — see <see cref="IsComplete"/>, which is
/// the same check a paste makes and answered with the same sentence.
/// </remarks>
/// <param name="Name">
/// What the palette calls it: the name on the group inside, falling back to the
/// file's own name for a patch that was dropped into the folder by hand.
/// </param>
/// <param name="Path">The file it came from, which is also how it is removed.</param>
public sealed record SavedGroup(string Name, string Path, PatchLoad Load)
{
    public bool IsComplete => Load.IsComplete;

    /// <summary>What to add to a patch — an ordinary fragment, box and all.</summary>
    public Patch Fragment => Load.Patch;

    /// <summary>
    /// How many modules arrive with it, which is what a saved group is worth
    /// knowing before it is added. The sink is left out because pasting drops it.
    /// </summary>
    public int Modules => Fragment.Nodes.Count(node => !NodeCatalog.IsSink(node.TypeId));
}

/// <summary>
/// The groups somebody kept, as patch files in a folder of their own.
/// </summary>
/// <remarks>
/// <para>
/// A file per group and each one an ordinary patch
/// ([0045](0045-what-is-copied-is-a-patch-file.md)), so there is no library
/// format to invent, no index to keep in step with what is on the disk, and no
/// question about what a saved group <em>is</em>: it is the thing the clipboard
/// already carries, written where it can be found again. Which also means a
/// <c>.fbk</c> dropped into the folder by hand is on the palette next time it is
/// opened, and a group saved here can be mailed to somebody.
/// </para>
/// <para>
/// Nothing here is load-bearing. A folder that will not read is an empty
/// palette section and not a failure to start, on the same terms
/// <see cref="Assist.AssistantSettings"/> keeps. Writing is the exception and
/// throws, because silently failing to keep what somebody just asked to keep is
/// worse than a line in the status bar.
/// </para>
/// </remarks>
public sealed class GroupLibrary
{
    /// <summary>
    /// Beside the settings ADR-0034 put in this folder, in a folder of its own
    /// because these are documents rather than preferences — there may be many,
    /// they are individually named, and a person may well want to look at them.
    /// </summary>
    public static string DefaultFolder => Path.Combine(GlobalConstants.DataFolder, "groups");

    private readonly ModuleCatalog catalog;
    private List<SavedGroup> kept = [];

    /// <param name="catalog">What the fragments are read against, so a missing plugin is named.</param>
    /// <param name="folder">Somewhere other than the usual place, for the tests.</param>
    public GroupLibrary(ModuleCatalog catalog, string? folder = null)
    {
        this.catalog = catalog;
        Folder = folder ?? DefaultFolder;

        Reload();
    }

    public string Folder { get; }

    /// <summary>
    /// What is on the disk as of the last <see cref="Reload"/>, by name.
    /// </summary>
    /// <remarks>
    /// Held rather than read on demand, because the palette rebuilds its list on
    /// every keystroke in the filter box and parsing a folder of patches per
    /// letter typed would be a folder of patches parsed per letter typed.
    /// </remarks>
    public IReadOnlyList<SavedGroup> All => kept;

    /// <summary>Reads the folder again. Never throws; an unreadable file is skipped.</summary>
    public void Reload()
    {
        var found = new List<SavedGroup>();

        try
        {
            if (Directory.Exists(Folder))
                foreach (var file in Directory.EnumerateFiles(Folder, $"*.{PatchIO.FileExtension}"))
                    if (Read(file) is { } entry)
                        found.Add(entry);
        }
        catch (Exception)
        {
            // A folder that cannot be listed is a palette without this section,
            // which is exactly what it looked like before anybody saved one.
        }

        kept = [.. found.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// What is kept under <paramref name="name"/>, or null where nothing is.
    /// </summary>
    /// <remarks>
    /// The question <see cref="Save"/> answers silently by replacing, asked out
    /// loud — so whatever is about to call it can say what will happen first.
    /// Compared the way a file name is: two groups whose names differ only in
    /// case are one entry here, because they would be one file on the disk.
    /// </remarks>
    public SavedGroup? Named(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : kept.FirstOrDefault(entry => Same(entry.Name, name));

    /// <summary>
    /// Keeps <paramref name="group"/> and everything in it, replacing whatever
    /// was kept under the same name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replacing rather than making a second entry, because that is what saving
    /// something under a name it already has means everywhere else — and two
    /// rows reading "Voice" would be a list that cannot be used to tell them
    /// apart.
    /// </para>
    /// <para>
    /// Kept shut, whatever it was when it was kept. A box is the whole of what a
    /// kept group is for — one thing to drop into a patch — and an open one
    /// arrives as a heap of modules with a dashed line round them, which is the
    /// same modules and none of the point. Opening one after it lands is a
    /// double-click; finding the box it was meant to be is not.
    /// </para>
    /// </remarks>
    /// <param name="group">The box to keep. Its name is what the palette will call it.</param>
    /// <param name="patch">Where its modules are now. Not modified.</param>
    /// <exception cref="InvalidOperationException">The group has no name to be listed under.</exception>
    public SavedGroup Save(NodeGroup group, Patch patch)
    {
        if (string.IsNullOrWhiteSpace(group.Name))
            throw new InvalidOperationException("A group is listed by its name, and this one has none.");

        // The clipboard's own copy, which brings the box with the modules —
        // every member is named, so the group comes whole. See PatchClipboard.
        var fragment = PatchClipboard.Copy(patch, group.Members);

        // Shut on the way out. The copy above is a deep one, so this says
        // nothing about the group on the canvas: it stays open if that is how it
        // was being worked on.
        foreach (var box in fragment.Groups ?? []) box.Collapsed = true;

        Directory.CreateDirectory(Folder);

        var path = Named(group.Name)?.Path ?? FreshPath(group.Name);

        File.WriteAllText(path, PatchIO.ToJson(fragment, catalog));
        Reload();

        return kept.FirstOrDefault(entry => entry.Path == path)
            ?? new SavedGroup(group.Name, path, PatchIO.Read(File.ReadAllText(path), catalog));
    }

    /// <summary>Forgets one, which is deleting the file it was.</summary>
    /// <returns>Whether it was there to remove.</returns>
    public bool Remove(SavedGroup entry)
    {
        var went = false;

        if (File.Exists(entry.Path))
        {
            File.Delete(entry.Path);
            went = true;
        }

        Reload();
        return went;
    }

    private SavedGroup? Read(string file)
    {
        try
        {
            var load = PatchIO.Read(File.ReadAllText(file), catalog);

            // The name on the box inside, because that is the name somebody
            // typed and a file name has been through a sieve to get here. A
            // patch with no group in it — one dropped into the folder by hand —
            // is listed under the file's own name and pastes as loose modules,
            // which is a thing worth being able to do rather than an error.
            var named = load.Patch.Groups?.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Name))?.Name;

            return new SavedGroup(named ?? Path.GetFileNameWithoutExtension(file), file, load);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A file for a name nothing is kept under yet.
    /// </summary>
    /// <remarks>
    /// The name is a title and a file name is not, so what goes on the disk is
    /// whatever survives the sieve — and where that collides with a file already
    /// there, a number. The name shown never comes from here: it is read back out
    /// of the patch, so a group called "In/Out" is listed as "In/Out" however its
    /// file had to be spelled.
    /// </remarks>
    private string FreshPath(string name)
    {
        var stem = new string([.. name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]).Trim();

        if (stem.Length == 0) stem = "group";

        var path = Path.Combine(Folder, $"{stem}.{PatchIO.FileExtension}");

        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(Folder, $"{stem} {n}.{PatchIO.FileExtension}");

        return path;
    }

    private static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
}

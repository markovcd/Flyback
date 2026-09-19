using System.Diagnostics;
using System.Text.Json;
using Flyback.Core;

namespace Flyback.App;

/// <summary>
/// Unsaved work as it stood a moment ago, written where the next start can find it
/// should this one never get as far as asking whether to save.
/// </summary>
/// <param name="Name">What the title bar called it, or null for a document with no name.</param>
/// <param name="Beside">The folder its sounds and pictures were measured from, or null.</param>
/// <param name="Patch">The patch on the canvas, as <see cref="Core.Graph.PatchIO.ToJson"/> writes it.</param>
/// <param name="Source">The text, where the text was the document (ADR-0068), and null where it was not.</param>
/// <param name="Conversation">The conversation about it, as the assistant saves one (ADR-0072).</param>
/// <param name="Files">What an open bundle was carrying, and null for a document that was not one.</param>
public sealed record RecoveredWork(
    string? Name,
    string? Beside,
    string Patch,
    string? Source,
    string? Conversation,
    IReadOnlyDictionary<string, byte[]>? Files);

/// <summary>
/// Where one window keeps its <see cref="RecoveredWork"/>, and how a start finds
/// what a window that crashed left behind — ADR-0103.
/// </summary>
/// <remarks>
/// Each window keeps a file of its own beside a lock of its own, and holds the lock
/// open for as long as it runs. The operating system lets go of it however the
/// process ends, so a snapshot whose lock can be taken is one nobody is keeping any
/// more: that is the whole of how a crash is told from a second copy of the program
/// that is still running. A window that closes normally deletes both.
/// <para>
/// Never throws. Losing the snapshot is losing a safety net, and a program that
/// stopped because it could not write one would be a worse crash than the one it
/// was there for.
/// </para>
/// </remarks>
internal sealed class Recovery : IDisposable
{
    private const string Extension = ".json";
    private const string LockExtension = ".lock";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Where the program itself keeps them.</summary>
    public static string Folder => Path.Combine(GlobalConstants.DataFolder, "recovery");

    private readonly string file;
    private readonly FileStream held;

    /// <summary>
    /// Serializes the writes, which arrive from the thread pool, and keeps a newer
    /// one from being written over by an older one that was slower to get here.
    /// </summary>
    private readonly Lock gate = new();

    private long asked;
    private long written;

    private Recovery(string file, FileStream held)
    {
        this.file = file;
        this.held = held;
    }

    /// <summary>A store of its own in <paramref name="folder"/>, or null where there cannot be one.</summary>
    public static Recovery? Open(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            var id = Guid.NewGuid().ToString("N");
            var held = new FileStream(
                Path.Combine(folder, id + LockExtension),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);

            return new Recovery(Path.Combine(folder, id + Extension), held);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"recovery: not kept this run: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Replaces what is kept with <paramref name="work"/>. Written beside the file and
    /// moved over it, so a crash in the middle of writing leaves the last whole one.
    /// </summary>
    /// <remarks>Safe from any thread; the latest call to be made wins, whichever finishes last.</remarks>
    public void Keep(RecoveredWork? work) => Later(work)();

    /// <summary>
    /// What <see cref="Keep"/> would do, numbered now so its place in line is kept,
    /// to be done off the thread that asked. Null keeps nothing, for a document with
    /// nothing left to lose.
    /// </summary>
    public Action Later(RecoveredWork? work)
    {
        var turn = Interlocked.Increment(ref asked);

        return () => Write(turn, work);
    }

    private void Write(long turn, RecoveredWork? work)
    {
        lock (gate)
        {
            if (turn < written) return;

            written = turn;

            try
            {
                if (work is null)
                {
                    File.Delete(file);
                    return;
                }

                var writing = file + ".tmp";

                File.WriteAllBytes(writing, JsonSerializer.SerializeToUtf8Bytes(work, Options));
                File.Move(writing, file, overwrite: true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"recovery: could not write: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The snapshots in <paramref name="folder"/> that nothing running is keeping, the
    /// most recently written first.
    /// </summary>
    /// <remarks>
    /// A lock with no snapshot beside it is a window that crashed with nothing to
    /// lose, and is tidied away here.
    /// </remarks>
    public static IReadOnlyList<string> Orphans(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];

            foreach (var stale in Directory.EnumerateFiles(folder, "*" + LockExtension))
            {
                if (!File.Exists(Path.ChangeExtension(stale, Extension)) && Unheld(stale)) Delete(stale);
            }

            return
            [
                .. Directory.EnumerateFiles(folder, "*" + Extension)
                    .Where(path => Unheld(Path.ChangeExtension(path, LockExtension)))
                    .OrderByDescending(File.GetLastWriteTimeUtc),
            ];
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"recovery: could not look: {ex.Message}");
            return [];
        }
    }

    /// <summary>What an orphan holds, or null for one that cannot be read.</summary>
    public static RecoveredWork? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<RecoveredWork>(File.ReadAllBytes(path), Options);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"recovery: could not read {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Deletes an orphan and its lock, once it has been restored or refused.</summary>
    public static void Forget(string path)
    {
        Delete(path);
        Delete(path + ".tmp");
        Delete(Path.ChangeExtension(path, LockExtension));
    }

    /// <summary>Lets go of the lock and leaves the snapshot where it is, which is what a crash does.</summary>
    internal void Abandon() => held.Dispose();

    /// <summary>A window closing: whatever it was asked about has been answered, so nothing is kept.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            written = long.MaxValue;

            Delete(file);
            held.Dispose();
            Delete(Path.ChangeExtension(file, LockExtension));
        }
    }

    /// <summary>Whether nothing is holding <paramref name="lockFile"/>, which a missing one is not.</summary>
    private static bool Unheld(string lockFile)
    {
        try
        {
            using var taken = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"recovery: could not delete {Path.GetFileName(path)}: {ex.Message}");
        }
    }
}

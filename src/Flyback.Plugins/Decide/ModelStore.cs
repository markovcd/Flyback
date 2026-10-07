using System.Security.Cryptography;
using Flyback.Core;

namespace Flyback.Plugins.Decide;

/// <summary>
/// Where a decision model's files are kept, one folder per model, and the one way they get
/// there: downloaded beside, hashed on the way in, and moved into place only when the hash is the pinned one.
/// </summary>
internal sealed class ModelStore(string? root)
{
    /// <summary>Where this machine keeps models.</summary>
    public static string DefaultRoot => Path.Combine(GlobalConstants.DataFolder, "models");

    /// <summary>Where models are kept, or null where none are, which leaves every model that needs files unprepared.</summary>
    public string? Root => root;

    /// <summary>The folder <paramref name="model"/>'s files go in, or null for an id that cannot name one.</summary>
    public string? FolderOf(IDecisionModel model) => root is not null && FolderName(model.Id) ? Path.Combine(root, model.Id) : null;

    /// <summary>Whether <paramref name="model"/> needs nothing it does not already have.</summary>
    public bool Prepared(IDecisionModel model)
    {
        if (model is not IPreparedModel prepared) return true;
        if (FolderOf(model) is not { } folder) return false;

        try
        {
            return prepared.Prepared(folder);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>How many bytes <paramref name="model"/> still has to download.</summary>
    public long Missing(IDecisionModel model) =>
        model is IPreparedModel prepared && FolderOf(model) is { } folder
            ? prepared.Needs.Where(f => !Present(folder, f)).Sum(f => f.Size)
            : 0;

    /// <summary>
    /// Downloads every file <paramref name="model"/> needs that is not here yet, telling
    /// <paramref name="progress"/> the bytes done so far out of the bytes to do.
    /// </summary>
    /// <exception cref="InvalidDataException">A file was refused: a name that is no plain file name, a hash or a size that is not the pinned one.</exception>
    /// <exception cref="HttpRequestException">A file could not be fetched.</exception>
    public async Task Prepare(IDecisionModel model, HttpClient http, IProgress<(long Done, long Total)>? progress, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(http);

        if (model is not IPreparedModel prepared) return;

        var folder = FolderOf(model) ?? throw new InvalidDataException(root is null ? "There is nowhere to keep a model." : $"'{model.Id}' cannot name a folder.");
        var wanted = prepared.Needs.Where(f => !Present(folder, f)).ToList();

        foreach (var file in wanted)
        {
            if (!FileName(file.Name)) throw new InvalidDataException($"'{file.Name}' is not a plain file name, so it is not written.");
            if (file.Size <= 0) throw new InvalidDataException($"'{file.Name}' has no size to hold its download to.");
            if (file.Address.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException($"'{file.Name}' is not offered over https.");
        }

        Directory.CreateDirectory(folder);

        var total = wanted.Sum(f => f.Size);
        long before = 0;

        foreach (var file in wanted)
        {
            var done = before;
            await Fetch(file, folder, http, new Progress<long>(bytes => progress?.Report((done + bytes, total))), cancel).ConfigureAwait(false);
            before += file.Size;
            progress?.Report((before, total));
        }
    }

    /// <summary>Writes <paramref name="file"/> beside its place, and moves it in only once it hashes as pinned.</summary>
    internal static async Task Fetch(ModelFile file, string folder, HttpClient http, IProgress<long>? progress, CancellationToken cancel)
    {
        var path = Path.Combine(folder, file.Name);
        var partial = path + ".partial";

        using var response = await http.GetAsync(file.Address, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{file.Address.Host} answered {(int)response.StatusCode} for {file.Name}.");

        if (response.Content.Headers.ContentLength is { } length && length != file.Size)
            throw new InvalidDataException($"{file.Name} is {length:N0} bytes, not the {file.Size:N0} it was pinned at.");

        string hash;

        await using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
        using (var sha = SHA256.Create())
        {
            await using (var target = File.Create(partial))
            await using (var hashing = new CryptoStream(target, sha, CryptoStreamMode.Write))
                await Copy(source, hashing, file, progress, cancel).ConfigureAwait(false);

            hash = Convert.ToHexStringLower(sha.Hash!);
        }

        if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{file.Name} does not hash as it was pinned to, so it is not used.");

        File.Move(partial, path, overwrite: true);
    }

    private static async Task Copy(Stream source, Stream target, ModelFile file, IProgress<long>? progress, CancellationToken cancel)
    {
        var buffer = new byte[1 << 16];
        long total = 0;

        for (int read; (read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0;)
        {
            total += read;

            if (total > file.Size) throw new InvalidDataException($"{file.Name} ran past the {file.Size:N0} bytes it was pinned at.");

            await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            progress?.Report(total);
        }
    }

    /// <summary>Whether <paramref name="file"/> is in <paramref name="folder"/> at its size. The hash was checked on the way in.</summary>
    private static bool Present(string folder, ModelFile file)
    {
        try
        {
            return FileName(file.Name) && new FileInfo(Path.Combine(folder, file.Name)) is { Exists: true } found && found.Length == file.Size;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A name that stays in the folder it is put in.</summary>
    internal static bool FileName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not "." and not ".."
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>An id that is safe to name a folder with.</summary>
    internal static bool FolderName(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && id is not "." and not "..";
}

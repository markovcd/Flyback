namespace Flyback.Cli.Rendering;

/// <summary>
/// Puts a preset's files into the share the site serves, each whole or not at all.
/// </summary>
/// <remarks>
/// Written under a temporary name and renamed into place, so the site never serves
/// half a file. <c>{id}.done</c> goes last and says the rest are there.
/// </remarks>
internal sealed class MediaWriter(string root) : IPresetMedia
{
    public string Root { get; } = root;

    public bool Pending(string id) =>
        !File.Exists(Path.Combine(Root, id + ".done")) && !File.Exists(Path.Combine(Root, id + ".failed"));

    public Task Put(string id, string suffix, string from, CancellationToken cancellation)
    {
        var to = Path.Combine(Root, id + suffix);
        var partial = to + ".tmp";

        File.Copy(from, partial, overwrite: true);
        File.Move(partial, to, overwrite: true);

        return Task.CompletedTask;
    }

    public Task Done(string id, CancellationToken cancellation)
    {
        Write(id, ".done", []);
        return Task.CompletedTask;
    }

    public Task Failed(string id, string why, CancellationToken cancellation)
    {
        Write(id, ".failed", System.Text.Encoding.UTF8.GetBytes(why));
        return Task.CompletedTask;
    }

    public void Write(string id, string suffix, byte[] bytes)
    {
        var to = Path.Combine(Root, id + suffix);
        var partial = to + ".tmp";

        File.WriteAllBytes(partial, bytes);
        File.Move(partial, to, overwrite: true);
    }
}

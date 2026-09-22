namespace Flyback.Cli;

/// <summary>
/// Puts a preset's files into the share the site serves, each whole or not at all.
/// </summary>
/// <remarks>
/// Written under a temporary name and renamed into place, so the site never serves
/// half a file. <c>{id}.done</c> goes last and says the rest are there.
/// </remarks>
internal sealed class MediaWriter(string root)
{
    public string Root { get; } = root;

    public bool Pending(string id) =>
        !File.Exists(Path.Combine(Root, id + ".done")) && !File.Exists(Path.Combine(Root, id + ".failed"));

    public void Put(string id, string suffix, string from)
    {
        var to = Path.Combine(Root, id + suffix);
        var partial = to + ".tmp";

        File.Copy(from, partial, overwrite: true);
        File.Move(partial, to, overwrite: true);
    }

    public void Put(string id, string suffix, byte[] bytes)
    {
        var to = Path.Combine(Root, id + suffix);
        var partial = to + ".tmp";

        File.WriteAllBytes(partial, bytes);
        File.Move(partial, to, overwrite: true);
    }

    public void Done(string id) => Put(id, ".done", []);

    public void Failed(string id, string why) => Put(id, ".failed", System.Text.Encoding.UTF8.GetBytes(why));
}

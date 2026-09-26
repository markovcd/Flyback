namespace Flyback.Plugins.Hosting;

/// <summary>One file a package would put into the plugin's folder.</summary>
/// <param name="Path">Where it goes, relative to the plugin's folder, with forward slashes.</param>
/// <param name="Index">Its place among the zip's entries.</param>
/// <param name="Length">How long the zip says it is.</param>
internal sealed record PackedFile(string Path, int Index, long Length);
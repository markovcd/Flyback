namespace Flyback.Plugins.Hosting;

/// <summary>A resource an assembly embeds under a preview's name, without its bytes where it is too large to be one.</summary>
internal sealed record EmbeddedPreview(string Name, long Length, byte[]? Bytes);
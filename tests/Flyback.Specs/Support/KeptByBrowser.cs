using Flyback.Editor;

namespace Flyback.Specs.Support;

/// <summary>A browser's local storage, for a page's settings.</summary>
internal sealed class KeptByBrowser : IBrowserStore
{
    private readonly Dictionary<string, string> kept = [];

    public string? Read(string section) => kept.GetValueOrDefault(section);

    public void Write(string section, string json) => kept[section] = json;
}

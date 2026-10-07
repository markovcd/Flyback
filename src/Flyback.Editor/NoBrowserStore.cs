namespace Flyback.Editor;

/// <summary>Outside a page: settings live in the settings file instead.</summary>
internal sealed class NoBrowserStore : IBrowserStore
{
    public string? Read(string section) => null;

    public void Write(string section, string json)
    {
    }
}

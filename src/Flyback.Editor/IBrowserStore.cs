namespace Flyback.Editor;

/// <summary>Settings a browser keeps for its page between visits, by section.</summary>
internal interface IBrowserStore
{
    /// <summary>The section as last written, or null if this browser has none.</summary>
    string? Read(string section);

    /// <summary>Never throws: a browser that keeps nothing simply forgets.</summary>
    void Write(string section, string json);
}

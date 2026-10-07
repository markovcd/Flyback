using System.Runtime.InteropServices.JavaScript;
using Flyback.Editor;

namespace Flyback.Editor.Web;

/// <summary>The page's settings, in the browser's local storage.</summary>
internal sealed partial class PageStore : IBrowserStore
{
    public string? Read(string section) => ReadSetting(section);

    public void Write(string section, string json) => WriteSetting(section, json);

    [JSImport("readSetting", PageModule.Name)]
    private static partial string? ReadSetting(string section);

    [JSImport("writeSetting", PageModule.Name)]
    private static partial void WriteSetting(string section, string json);
}

using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;

[assembly: SupportedOSPlatform("browser")]

namespace Flyback.WebEditor;

internal static class Program
{
    private static Task Main() => AppBuilder.Configure<PageApp>().WithInterFont().StartBrowserAppAsync("out");
}

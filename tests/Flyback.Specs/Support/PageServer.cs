using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Flyback.Specs.Support;

/// <summary>
/// The website on loopback as the preset site lays it out: the web viewer's build at
/// <c>/viewer/</c>, the web editor's at <c>/editor/</c>, files a scenario offers at
/// <c>/files/</c>, and the rest of <c>site/</c>.
/// </summary>
internal sealed class PageServer : IDisposable
{
    private static readonly Lazy<StaticAssets> Viewer = new(() => Build("WebViewer", "Flyback.Viewer.Web"));
    private static readonly Lazy<StaticAssets> Editor = new(() => Build("WebEditor", "Flyback.Editor.Web"));

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript",
        [".mjs"] = "text/javascript",
        [".css"] = "text/css",
        [".json"] = "application/json",
        [".wasm"] = "application/wasm",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    private readonly HttpListener listener = new();
    private readonly ConcurrentDictionary<string, byte[]> offered = new();

    /// <summary>The site's root.</summary>
    public Uri Root { get; }

    public PageServer()
    {
        var port = FreePort();
        Root = new Uri($"http://127.0.0.1:{port}/");

        listener.Prefixes.Add(Root.AbsoluteUri);
        listener.Start();
        _ = Task.Run(Serve);
    }

    /// <summary>Serves <paramref name="bytes"/> at <c>/files/<paramref name="name"/></c>, and answers its address.</summary>
    public Uri Offer(string name, byte[] bytes)
    {
        offered[name] = bytes;
        return new Uri(Root, $"files/{Uri.EscapeDataString(name)}");
    }

    private async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext request;

            try
            {
                request = await listener.GetContextAsync();
            }
            catch (Exception stopped) when (stopped is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => Answer(request));
        }
    }

    private void Answer(HttpListenerContext context)
    {
        using var response = context.Response;
        var path = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath);

        var (named, body) = path.Split('/', 3) switch
        {
            ["", "files", var name] => (name, offered.GetValueOrDefault(name)),
            ["", "viewer", var rest] => Read(Viewer.Value.Find(Index(rest))),
            ["", "editor", var rest] => Read(Editor.Value.Find(Index(rest))),
            _ => Read(SiteFiles.Find(path)),
        };

        if (body is null)
        {
            response.StatusCode = 404;
            return;
        }

        response.ContentType = Types.GetValueOrDefault(Path.GetExtension(named), "application/octet-stream");
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = body.Length;

        try
        {
            if (context.Request.HttpMethod != "HEAD") response.OutputStream.Write(body);
        }
        catch (HttpListenerException)
        {
            // The page went away mid-answer, as one does when a scenario ends.
        }
    }

    private static string Index(string path) => path.Length == 0 || path.EndsWith('/') ? path + "index.html" : path;

    private static (string Name, byte[]? Body) Read(string? file) =>
        file is not null && File.Exists(file) ? (file, File.ReadAllBytes(file)) : ("", null);

    /// <summary>The static web assets of the build the specs project names under <paramref name="key"/>.</summary>
    private static StaticAssets Build(string key, string project)
    {
        var folder = typeof(PageServer).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == key).Value!;

        return StaticAssets.Read(Path.Combine(folder, $"{project}.staticwebassets.runtime.json"));
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        listener.Stop();
        listener.Close();
    }
}

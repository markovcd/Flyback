using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The web viewer's and the web editor's files as the preset site sends them to a browser.</summary>
[Binding]
public sealed partial class CompressedDownloadsSteps : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-downloads-").FullName;
    private string? editor;
    private WebApplicationFactory<Program>? host;
    private HttpClient? client;

    private string runtime = string.Empty;
    private byte[] sent = [];
    private HttpResponseMessage? response;

    private HttpClient Client
    {
        get
        {
            if (client is not null) return client;

            host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            {
                web.UseSetting("Site:Database", Path.Combine(folder, "presets.db"));
                web.UseSetting("Site:Defaults", Path.Combine(folder, "no-defaults"));
                web.UseSetting("Site:Media", Path.Combine(folder, "media"));
                if (editor is not null) web.UseSetting("Site:Editor", editor);
            });
            return client = host.CreateClient();
        }
    }

    /// <summary>A published editor's shape: a loader, and a fingerprinted runtime with its brotli and gzip copies.</summary>
    [Given("a web editor beside the preset site")]
    public void GivenAWebEditor()
    {
        editor = Path.Combine(folder, "editor");
        var framework = Directory.CreateDirectory(Path.Combine(editor, "_framework")).FullName;
        var wasm = Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i % 7)).ToArray();

        File.WriteAllText(Path.Combine(editor, "index.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(framework, "dotnet.js"), "\"dotnet.native.abcdefghij.wasm\"");
        File.WriteAllBytes(Path.Combine(framework, "dotnet.native.abcdefghij.wasm"), wasm);
        File.WriteAllBytes(Path.Combine(framework, "dotnet.native.abcdefghij.wasm.br"), Packed(wasm, s => new BrotliStream(s, CompressionLevel.Optimal)));
        File.WriteAllBytes(Path.Combine(framework, "dotnet.native.abcdefghij.wasm.gz"), Packed(wasm, s => new GZipStream(s, CompressionLevel.Optimal)));
    }

    [When("a browser that takes {string} fetches the web viewer's runtime")]
    public Task WhenTheViewerRuntimeIsFetched(string takes) => Fetch("/viewer", takes);

    [When("a browser that takes {string} fetches the web editor's runtime")]
    public Task WhenTheEditorRuntimeIsFetched(string takes) => Fetch("/editor", takes);

    [Then("it is sent the {string} copy, which unpacks to the file itself")]
    public async Task ThenTheCopyIsSent(string coding)
    {
        response!.Content.Headers.ContentEncoding.ShouldBe([coding], runtime);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/wasm", runtime);
        response.Headers.Vary.ShouldContain("Accept-Encoding", runtime);

        Stream packed = new MemoryStream(sent);
        using var unpacked = coding == "br" ? new BrotliStream(packed, CompressionMode.Decompress) : (Stream)new GZipStream(packed, CompressionMode.Decompress);
        using var file = new MemoryStream();
        await unpacked.CopyToAsync(file);

        sent.Length.ShouldBeLessThan((int)file.Length, runtime);
        file.ToArray().ShouldBe(await AsItLies(), runtime);
    }

    [Then("it is sent the file as it lies")]
    public async Task ThenTheFileIsSent()
    {
        response!.Content.Headers.ContentEncoding.ShouldBeEmpty(runtime);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/wasm", runtime);
        sent.ShouldBe(await AsItLies(), runtime);
    }

    [Then("the runtime is kept for good")]
    public void ThenTheRuntimeIsKept() => (response!.Headers.CacheControl?.MaxAge).ShouldBe(TimeSpan.FromDays(365), runtime);

    [Then("the web editor's loader is checked with the site before each use")]
    public async Task ThenTheEditorLoaderIsChecked()
    {
        using var loader = await Client.GetAsync(new Uri("/editor/_framework/dotnet.js", UriKind.Relative));
        (loader.Headers.CacheControl?.NoCache ?? false).ShouldBeTrue();
    }

    private async Task Fetch(string route, string takes)
    {
        var loader = await Client.GetStringAsync(new Uri($"{route}/_framework/dotnet.js", UriKind.Relative));
        runtime = $"{route}/_framework/{Runtime().Match(loader).Value}";

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(runtime, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept-Encoding", takes);
        response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        sent = await response.Content.ReadAsByteArrayAsync();
    }

    private Task<byte[]> AsItLies() => Client.GetByteArrayAsync(new Uri(runtime, UriKind.Relative));

    private static byte[] Packed(byte[] bytes, Func<Stream, Stream> packer)
    {
        using var packed = new MemoryStream();
        using (var stream = packer(packed)) stream.Write(bytes);
        return packed.ToArray();
    }

    [GeneratedRegex(@"dotnet\.native\.[a-z0-9]{10}\.wasm")]
    private static partial Regex Runtime();

    public void Dispose()
    {
        response?.Dispose();
        client?.Dispose();
        host?.Dispose();
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }
}

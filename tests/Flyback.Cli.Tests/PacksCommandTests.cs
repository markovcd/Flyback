using System.CommandLine;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Tests;

/// <summary><c>packs install</c> and <c>packs list</c>: putting a module pack where every editor finds it, from a script.</summary>
public sealed class PacksCommandTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-cli-packs-{Guid.NewGuid():N}")).FullName;

    private string Folder => Path.Combine(root, "packs");

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Written(string name, string text)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private const string Echoes = "pack ada.echoes 1.0.0\nmodule wobble(rate = 3) = sine(freq: rate)\n";

    [Fact]
    public void Installing_a_pack_copies_it_into_the_folder_and_names_its_modules()
    {
        var (code, output, _) = Run("packs", "install", Written("echoes.fbks", Echoes), "--folder", Folder, "--json");

        code.ShouldBe(Exit.Ok);

        var answer = JsonElement.Parse(output);
        answer.GetProperty("id").GetString().ShouldBe("ada.echoes");
        answer.GetProperty("modules")[0].GetString().ShouldBe("ada.echoes.wobble");
        File.ReadAllText(Path.Combine(Folder, "ada.echoes.fbks")).ShouldBe(Echoes);
    }

    [Fact]
    public void A_pack_with_a_mistake_fails_and_says_the_line()
    {
        var (code, _, error) = Run("packs", "install", Written("bad.fbks", "pack ada.bad 1.0.0\nmodule m(in = 0) = sine(fre: in)\n"), "--folder", Folder);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("bad.fbks: line 2:");
        Directory.Exists(Folder).ShouldBeFalse();
    }

    [Fact]
    public void The_list_says_each_pack_and_each_problem()
    {
        Run("packs", "install", Written("echoes.fbks", Echoes), "--folder", Folder);
        File.WriteAllText(Path.Combine(Folder, "broken.fbks"), "pack ada.broken 1.0.0\nmodule bad(in = 0) = sine(fre: in)\n");

        var (code, output, _) = Run("packs", "list", "--folder", Folder, "--json");

        code.ShouldBe(Exit.Ok);

        var answer = JsonElement.Parse(output);
        answer.GetProperty("packs")[0].GetProperty("id").GetString().ShouldBe("ada.echoes");
        answer.GetProperty("problems")[0].GetProperty("file").GetString().ShouldBe("broken.fbks");
    }

    [Fact]
    public async Task Getting_a_pack_from_the_site_installs_its_newest_version()
    {
        using var output = new StringWriter();
        var cancel = TestContext.Current.CancellationToken;

        var code = await PacksCommand.Get(Shared(Echoes), PluginCatalog.Empty.Modules, "ada.echoes", Folder, json: true, output, TextWriter.Null, cancel);

        code.ShouldBe(Exit.Ok);
        JsonElement.Parse(output.ToString()).GetProperty("version").GetString().ShouldBe("1.0.0");
        File.ReadAllText(Path.Combine(Folder, "ada.echoes.fbks")).ShouldBe(Echoes);
    }

    [Fact]
    public async Task A_pack_the_site_does_not_share_fails_and_says_so()
    {
        using var error = new StringWriter();
        var cancel = TestContext.Current.CancellationToken;

        var code = await PacksCommand.Get(Shared(Echoes), PluginCatalog.Empty.Modules, "ada.other", Folder, json: false, TextWriter.Null, error, cancel);

        code.ShouldBe(Exit.Failed);
        error.ToString().ShouldContain("shares no pack ada.other");
        Directory.Exists(Folder).ShouldBeFalse();
    }

    [Fact]
    public async Task The_search_names_each_shared_pack_and_its_modules()
    {
        using var output = new StringWriter();

        var code = await PacksCommand.Search(Shared(Echoes), "wobble", json: false, output, TextWriter.Null, TestContext.Current.CancellationToken);

        code.ShouldBe(Exit.Ok);
        output.ToString().ShouldContain("ada.echoes 1.0.0: ada.echoes.wobble");
    }

    /// <summary>A site sharing one pack, ada.echoes, whatever it is asked.</summary>
    private static PackSite Shared(string text) => new(new HttpClient(new Site(text)), new Uri("https://presets.example.org/"));

    private sealed class Site(string text) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var asked = request.RequestUri!;
            var listing = asked.Query.Contains("pack=ada.other", StringComparison.Ordinal)
                ? """{ "items": [], "total": 0, "page": 1, "pageSize": 24 }"""
                : $$"""
                    { "items": [ { "id": "v1", "pack": "ada.echoes", "version": "1.0.0",
                      "sha256": "{{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))}}",
                      "file": "/api/v1/packs/v1/file", "modules": [ { "id": "ada.echoes.wobble", "name": "Wobble" } ] } ],
                      "total": 1, "page": 1, "pageSize": 24 }
                    """;

            return Task.FromResult(asked.AbsolutePath == "/api/v1/packs/v1/file"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listing, Encoding.UTF8, "application/json") });
        }
    }

    private (int Code, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var registry = new PluginRegistry(() => PluginCatalog.Empty, Path.Combine(root, PluginHost.DirectoryName), null);

        var code = Program.Run(args, registry, new InvocationConfiguration { Output = output, Error = error });

        return (code, output.ToString(), error.ToString());
    }
}

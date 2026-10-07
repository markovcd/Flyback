using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>A model's files arrive whole and as pinned, inside its own folder, or not at all.</summary>
public sealed class ModelStoreTests : IDisposable
{
    private static readonly byte[] Weights = Encoding.ASCII.GetBytes("the weights");

    private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flyback-models-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private ModelStore Store => new(root);

    [Fact]
    public async Task A_file_that_hashes_as_pinned_is_moved_into_place()
    {
        var model = new Needing(File("weights.bin"));

        await Store.Prepare(model, Http(Weights), null, TestContext.Current.CancellationToken);

        Store.Prepared(model).ShouldBeTrue();
        Store.Missing(model).ShouldBe(0);
        Directory.GetFiles(Path.Combine(root, "needing")).Select(Path.GetFileName).ShouldBe(["weights.bin"]);
    }

    [Fact]
    public async Task A_file_that_hashes_otherwise_leaves_only_its_partial()
    {
        var model = new Needing(File("weights.bin"));

        await Should.ThrowAsync<InvalidDataException>(
            Store.Prepare(model, Http(Encoding.ASCII.GetBytes("not weights")), null, TestContext.Current.CancellationToken));

        Store.Prepared(model).ShouldBeFalse();
        Directory.GetFiles(Path.Combine(root, "needing")).Select(Path.GetFileName).ShouldBe(["weights.bin.partial"]);
    }

    [Fact]
    public async Task A_download_longer_than_pinned_is_stopped()
    {
        var model = new Needing(File("weights.bin"));

        var refused = await Should.ThrowAsync<InvalidDataException>(
            Store.Prepare(model, Http([.. Weights, .. Weights], announceLength: false), null, TestContext.Current.CancellationToken));

        refused.Message.ShouldContain("ran past");
    }

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("..")]
    [InlineData("sub/weights.bin")]
    [InlineData("sub\\weights.bin")]
    [InlineData("")]
    public async Task A_name_that_is_not_a_plain_file_name_is_never_written(string name)
    {
        var model = new Needing(File(name));
        var http = new Counting(Weights);

        await Should.ThrowAsync<InvalidDataException>(Store.Prepare(model, new HttpClient(http), null, TestContext.Current.CancellationToken));

        http.Requests.ShouldBe(0);
        Directory.GetFileSystemEntries(root).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_offered_over_plain_http_is_refused()
    {
        var model = new Needing(File("weights.bin") with { Address = new Uri("http://models.test/weights.bin") });

        (await Should.ThrowAsync<InvalidDataException>(Store.Prepare(model, Http(Weights), null, TestContext.Current.CancellationToken)))
            .Message.ShouldContain("https");
    }

    [Theory]
    [InlineData("../up")]
    [InlineData("a/b")]
    [InlineData("")]
    public void An_id_that_cannot_name_a_folder_gets_none(string id)
    {
        Store.FolderOf(new Needing(File("w")) { Named = id }).ShouldBeNull();
    }

    private static ModelFile File(string name) =>
        new(new Uri("https://models.test/" + Uri.EscapeDataString(name)), name, Convert.ToHexStringLower(SHA256.HashData(Weights)), Weights.Length);

    private static HttpClient Http(byte[] body, bool announceLength = true) => new(new Counting(body, announceLength));

    private sealed class Counting(byte[] body, bool announceLength = true) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests++;

            HttpContent content = announceLength ? new ByteArrayContent(body) : new StreamContent(new MemoryStream(body));

            if (!announceLength) content.Headers.ContentLength = null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class Needing(params ModelFile[] files) : IDecisionModel, IPreparedModel
    {
        public string Named { get; init; } = "needing";

        public string Id => Named;

        public string Name => "Needing";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<ModelFile> Needs => files;

        public bool Prepared(string folder) => files.All(f => System.IO.File.Exists(Path.Combine(folder, f.Name)));

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) => throw new NotSupportedException();
    }
}

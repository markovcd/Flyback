using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Shouldly;
using Xunit;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Cli.Rendering;

namespace Flyback.Cli.Tests;

/// <summary>
/// Stands in for the renderer and ffmpeg: writes a file wherever one is asked
/// for, and answers the loudness pass as told.
/// </summary>
internal sealed class FakeTools : IPresetTools
{
    public string? FailOn { get; init; }

    public string Loudness { get; init; } = "-18.5";

    public List<string> Ran { get; } = [];

    public List<Opened> Rendered { get; } = [];

    public int Render(Opened patch, RenderOptions options, TextWriter error, CancellationToken cancellation)
    {
        Ran.Add("render " + options.Out.Name);
        Rendered.Add(patch);

        if (FailOn is not null && options.Out.Name.Contains(FailOn, StringComparison.Ordinal))
        {
            error.WriteLine("refusing to render a patch with errors in it.");
            return Exit.Problems;
        }

        File.WriteAllText(options.Out.FullName, options.Out.Name);

        return Exit.Ok;
    }

    public Task<Ran> Ffmpeg(IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var line = "ffmpeg " + string.Join(' ', arguments);
        Ran.Add(line);

        if (FailOn is not null && line.Contains(FailOn, StringComparison.Ordinal))
            return Task.FromResult(new Ran(1, [], "Unknown encoder"));

        if (line.Contains("print_format=json", StringComparison.Ordinal))
            return Task.FromResult(new Ran(0, [], $$"""
                [Parsed_loudnorm_0 @ 0x1]
                {
                    "input_i" : "{{Loudness}}",
                    "input_tp" : "-3.20",
                    "input_lra" : "5.10",
                    "input_thresh" : "-28.90",
                    "target_offset" : "0.40"
                }
                """));

        if (arguments[^1] == "-")
        {
            var samples = new float[8000];
            for (var i = 0; i < samples.Length; i++) samples[i] = MathF.Sin(i * 0.1f) * (i < 4000 ? 0.2f : 0.8f);

            var raw = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, raw, 0, raw.Length);

            return Task.FromResult(new Ran(0, raw, ""));
        }

        File.WriteAllText(arguments[^1], line);

        return Task.FromResult(new Ran(0, [], ""));
    }
}

public sealed class RenderPresetsTests : IDisposable
{
    private const string Picture = "rings(freq: 3, offset: t) |> color.hsv(saturation: _, hue: 0.3) |> out.color\n";
    private const string Sound = "sine(freq: 110) |> out.left\n";

    private readonly string work = Directory.CreateTempSubdirectory("flyback-presets-").FullName;
    private readonly string media;

    public RenderPresetsTests()
    {
        media = Directory.CreateDirectory(Path.Combine(work, "media")).FullName;
    }

    public void Dispose() => Directory.Delete(work, recursive: true);

    private FileInfo Patch(string text)
    {
        var file = new FileInfo(Path.Combine(work, Path.GetRandomFileName() + ".fbks"));
        File.WriteAllText(file.FullName, text);

        return file;
    }

    /// <summary>A saved patch naming a module nothing here provides.</summary>
    private FileInfo PatchShortOfAPlugin()
    {
        var load = Engine.Language.PatchLanguage.Build(Sound);
        var json = JsonNode.Parse(PatchIO.ToJson(load.Patch))!;
        json["Nodes"]!.AsArray().Add(new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["TypeId"] = "effects.acid" });

        var file = new FileInfo(Path.Combine(work, "short.fbk"));
        File.WriteAllText(file.FullName, json.ToJsonString());

        return file;
    }

    private async Task<string?> Render(FakeTools tools, FileInfo patch, string id = "abc") =>
        await new PresetRender(tools, new MediaWriter(media)).Render(id, patch, TestContext.Current.CancellationToken);

    private string[] Written() =>
        [.. Directory.GetFiles(media).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    [Fact]
    public async Task A_patch_with_a_picture_and_a_sound_gets_a_still_a_loop_and_a_track()
    {
        (await Render(new FakeTools(), Patch(Picture + Sound))).ShouldBeNull();

        Written().ShouldBe(["abc.done", "abc.mp3", "abc.peaks.json", "abc.webm", "abc.webp"]);
    }

    [Fact]
    public async Task A_patch_that_only_makes_a_picture_gets_no_track()
    {
        var tools = new FakeTools();

        (await Render(tools, Patch(Picture))).ShouldBeNull();

        Written().ShouldBe(["abc.done", "abc.webm", "abc.webp"]);
        tools.Ran.ShouldNotContain(line => line.Contains(".wav", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_patch_that_only_makes_a_sound_gets_no_picture()
    {
        (await Render(new FakeTools(), Patch(Sound))).ShouldBeNull();

        Written().ShouldBe(["abc.done", "abc.mp3", "abc.peaks.json"]);
    }

    [Fact]
    public async Task A_silent_patch_gets_no_track()
    {
        (await Render(new FakeTools { Loudness = "-inf" }, Patch(Sound))).ShouldBeNull();

        Written().ShouldBe(["abc.done"]);
    }

    /// <summary>
    /// A shared preset is somebody else's file. Naming a picture on this machine would
    /// publish it as the preset's still, so a loose one reads nothing off the disk.
    /// </summary>
    [Fact]
    public async Task A_preset_cannot_show_a_picture_off_the_render_machines_disk()
    {
        var picture = Path.Combine(work, "private.png");
        PngWriter.WriteBgra(picture, new byte[4 * 4 * 4], 4, 4, 16);

        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.Current);

        var shown = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.PictureTypeId), 0, 0);
        PictureExtra.Set(shown, picture);
        patch.Nodes.Add(shown);
        patch.Connect(shown.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        var file = new FileInfo(Path.Combine(work, "shown.fbk"));
        File.WriteAllText(file.FullName, PatchIO.ToJson(patch));

        var tools = new FakeTools();
        await Render(tools, file);

        tools.Rendered.ShouldNotBeEmpty();
        tools.Rendered.ShouldAllBe(opened => opened.Pictures.Find(picture) == null);
    }

    [Fact]
    public async Task A_patch_missing_a_plugin_here_is_marked_failed_rather_than_rendered_without_it()
    {
        var tools = new FakeTools();

        (await Render(tools, PatchShortOfAPlugin())).ShouldNotBeNull();

        Written().ShouldBe(["abc.failed"]);
        File.ReadAllText(Path.Combine(media, "abc.failed")).ShouldContain("effects.acid");
        tools.Ran.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_render_that_fails_is_marked_failed_with_what_the_renderer_said()
    {
        (await Render(new FakeTools { FailOn = "still.png" }, Patch(Picture))).ShouldNotBeNull();

        Written().ShouldBe(["abc.failed"]);
        File.ReadAllText(Path.Combine(media, "abc.failed")).ShouldContain("refusing to render");
    }

    [Fact]
    public async Task A_failure_part_way_leaves_no_media_behind()
    {
        (await Render(new FakeTools { FailOn = "libmp3lame" }, Patch(Picture + Sound))).ShouldNotBeNull();

        Written().ShouldBe(["abc.failed"]);
    }

    [Fact]
    public async Task The_track_is_brought_to_the_sites_loudness_with_what_the_first_pass_measured()
    {
        var tools = new FakeTools();

        await Render(tools, Patch(Sound));

        tools.Ran.ShouldContain(line => line.Contains("loudnorm=I=-16:TP=-1.5:LRA=11:measured_I=-18.5:measured_TP=-3.20", StringComparison.Ordinal)
            && line.Contains("afade=t=out:st=26:d=4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pass_renders_what_the_site_is_waiting_on_and_does_not_count_it_as_a_download()
    {
        var asked = new List<string>();
        var done = Guid.NewGuid().ToString("N");
        var waiting = Guid.NewGuid().ToString("N");

        File.WriteAllText(Path.Combine(media, done + ".done"), "");

        using var site = new HttpClient(new Site(asked, Sound, done, waiting)) { BaseAddress = new Uri("http://site/") };
        var render = new PresetRender(new FakeTools(), new MediaWriter(media));

        await RenderPresetsCommand.Pass(site, render, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken);

        asked.ShouldBe(["/api/v1/presets?pending=true", $"/api/v1/presets/{waiting}/file?count=false"]);
        File.Exists(Path.Combine(media, waiting + ".done")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_pass_with_a_limit_renders_that_many_and_leaves_the_rest_waiting()
    {
        var asked = new List<string>();
        string[] waiting = ["a", "b", "c"];

        using var site = new HttpClient(new Site(asked, Sound, waiting)) { BaseAddress = new Uri("http://site/") };
        var render = new PresetRender(new FakeTools(), new MediaWriter(media));

        await RenderPresetsCommand.Pass(site, render, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken, limit: 2);

        waiting.Where(id => File.Exists(Path.Combine(media, id + ".done"))).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task A_rendered_preset_is_no_longer_pending_and_neither_is_a_failed_one()
    {
        var writer = new MediaWriter(media);

        writer.Pending("a").ShouldBeTrue();

        await writer.Done("a", TestContext.Current.CancellationToken);
        await writer.Failed("b", "no", TestContext.Current.CancellationToken);

        writer.Pending("a").ShouldBeFalse();
        writer.Pending("b").ShouldBeFalse();
    }

    [Fact]
    public void A_file_is_put_in_place_whole_and_leaves_no_partial_copy()
    {
        new MediaWriter(media).Write("a", ".webp", [1, 2, 3]);

        Written().ShouldBe(["a.webp"]);
        File.ReadAllBytes(Path.Combine(media, "a.webp")).ShouldBe([1, 2, 3]);
    }

    /// <summary>The site's admin API, keeping what each upload was called and held.</summary>
    private sealed class Uploads : HttpMessageHandler
    {
        public List<(string Path, byte[] Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.ShouldBe(HttpMethod.Put);
            Sent.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsByteArrayAsync(cancellationToken)));

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task An_upload_sends_each_file_by_the_name_the_site_takes_and_done_last()
    {
        var uploads = new Uploads();
        using var site = new HttpClient(uploads) { BaseAddress = new Uri("http://site/") };

        (await new PresetRender(new FakeTools(), new MediaUpload(site)).Render("abc", Patch(Picture + Sound), TestContext.Current.CancellationToken))
            .ShouldBeNull();

        uploads.Sent.Select(s => s.Path).ShouldBe([
            "/api/v1/admin/presets/abc/media/webp",
            "/api/v1/admin/presets/abc/media/webm",
            "/api/v1/admin/presets/abc/media/mp3",
            "/api/v1/admin/presets/abc/media/peaks.json",
            "/api/v1/admin/presets/abc/media/done",
        ]);
        Encoding.UTF8.GetString(uploads.Sent[0].Body).ShouldContain("libwebp", customMessage: "the still is the webp ffmpeg made");
        uploads.Sent[^1].Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_upload_of_a_failed_render_says_why()
    {
        var uploads = new Uploads();
        using var site = new HttpClient(uploads) { BaseAddress = new Uri("http://site/") };

        await new PresetRender(new FakeTools { FailOn = "still.png" }, new MediaUpload(site)).Render("abc", Patch(Picture), TestContext.Current.CancellationToken);

        var (path, body) = uploads.Sent.Single();
        path.ShouldBe("/api/v1/admin/presets/abc/media/failed");
        Encoding.UTF8.GetString(body).ShouldContain("refusing to render");
    }

    [Fact]
    public void Peaks_are_the_loudness_of_each_slice_as_a_share_of_the_loudest()
    {
        var samples = new float[Peaks.Count * 10];
        for (var i = 0; i < samples.Length; i++) samples[i] = i < samples.Length / 2 ? 0.25f : 0.5f;

        var peaks = Peaks.Of(samples)!;

        peaks.Length.ShouldBe(Peaks.Count);
        peaks[0].ShouldBe(0.5);
        peaks[^1].ShouldBe(1);
    }

    [Fact]
    public void Quiet_slices_are_floored_so_every_bar_shows()
    {
        var samples = new float[Peaks.Count * 10];
        samples[^1] = 1;

        Peaks.Of(samples)![0].ShouldBe(0.1);
    }

    [Fact]
    public void Silence_has_no_peaks()
    {
        Peaks.Of(new float[Peaks.Count * 10]).ShouldBeNull();
    }

    /// <summary>The preset site's two answers the command reads: the waiting list and a file.</summary>
    private sealed class Site(List<string> asked, string patch, params string[] waiting) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            asked.Add(path);

            var body = path.Contains("pending", StringComparison.Ordinal)
                ? "{\"items\": [" + string.Join(',', waiting.Select(id => $"{{\"id\": \"{id}\", \"name\": \"P\", \"fileName\": \"p.fbks\"}}")) + "]}"
                : patch;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }
}

using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A patch opened from bytes, as a page or a site holds a file: the name's extension
/// says what the bytes are, and what is not a patch throws rather than opens.
/// </summary>
public class PatchFileTests
{
    [Fact]
    public void A_document_is_read_with_how_it_loaded()
    {
        var bundle = PatchFile.Read("notes.fbk", Encoding.UTF8.GetBytes(PatchIO.ToJson(Patched())));

        bundle.Patch.Nodes.Count.ShouldBe(1);
        bundle.Files.ShouldBeEmpty();
        bundle.Load.ShouldNotBeNull().IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void A_bundle_is_read_with_the_files_it_carries()
    {
        using var packed = new MemoryStream();
        PatchBundle.Write(packed, Patched(), _ => [1, 2, 3], NodeCatalog.BuiltIn);

        var bundle = PatchFile.Read("notes.fbkb", packed.ToArray());

        bundle.Patch.Nodes.Count.ShouldBe(1);
        bundle.Load.ShouldNotBeNull().IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void The_text_language_is_built()
    {
        var bundle = PatchFile.Read($"notes.{PatchLanguage.FileExtension}", Encoding.UTF8.GetBytes("expression(x) |> out.left"));

        bundle.Patch.Nodes.Count.ShouldBeGreaterThan(1);
        bundle.Load.ShouldBeNull();
    }

    [Fact]
    public void A_byte_order_mark_is_skipped() =>
        PatchFile.Read("notes.fbk", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(PatchIO.ToJson(Patched()))).ToArray())
            .Patch.Nodes.Count.ShouldBe(1);

    [Fact]
    public void Text_that_does_not_build_throws_with_its_issues() =>
        Should.Throw<InvalidDataException>(() => PatchFile.Read($"notes.{PatchLanguage.FileExtension}", Encoding.UTF8.GetBytes("|> |>")))
            .Message.ShouldNotBeEmpty();

    [Fact]
    public void A_document_that_is_not_json_throws() =>
        Should.Throw<JsonException>(() => PatchFile.Read("notes.fbk", Encoding.UTF8.GetBytes("not json at all")));

    [Fact]
    public void A_document_nested_past_the_readers_depth_throws_rather_than_recursing()
    {
        var nested = $"{{\"Nodes\": [{{\"TypeId\": \"coord\", \"State\": {new string('[', 100_000)}{new string(']', 100_000)}}}], \"Connections\": []}}";

        Should.Throw<JsonException>(() => PatchFile.Read("notes.fbk", Encoding.UTF8.GetBytes(nested)));
    }

    [Fact]
    public void Bytes_that_are_not_text_throw() =>
        Should.Throw<DecoderFallbackException>(() => PatchFile.Read("notes.fbk", [0xFF, 0xFE, 0x00, 0xC3]));

    [Fact]
    public void A_bundle_that_is_not_one_throws() =>
        Should.Throw<InvalidDataException>(() => PatchFile.Read("notes.fbkb", Encoding.UTF8.GetBytes("not a zip")));

    [Fact]
    public void A_document_from_a_later_version_reads_as_too_new()
    {
        var newer = PatchIO.ToJson(Patched()).Replace($"\"Version\": {PatchIO.FormatVersion}", $"\"Version\": {PatchIO.FormatVersion + 1}", StringComparison.Ordinal);

        PatchFile.Read("notes.fbk", Encoding.UTF8.GetBytes(newer)).Load.ShouldNotBeNull().TooNew.ShouldBeTrue();
    }

    private static Patch Patched()
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.BuiltIn);

        return patch;
    }
}

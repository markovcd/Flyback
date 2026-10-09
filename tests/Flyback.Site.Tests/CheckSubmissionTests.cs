using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Flyback.Engine.Graph;
using Flyback.Plugins.Hosting;
using Flyback.Site.Commands;
using Shouldly;
using Xunit;
using Flyback.Site.Checking;
using Flyback.Site.Reading;

namespace Flyback.Site.Tests;

public sealed class CheckSubmissionTests : IDisposable
{
    private static readonly BrowserPlugins Browser = BrowserPlugins.Linked();

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-site-check-");

    public void Dispose() => folder.Delete(recursive: true);

    private static PresetCheck Preset(byte[] file, string fileName = "Drone.fbk", string? name = null) =>
        (PresetCheck)Checks.Of(plugin: false, fileName, file, name, Browser, TextWriter.Null);

    private static PluginCheck Plugin(byte[] file, string fileName = "upload.fbkp") =>
        (PluginCheck)Checks.Of(plugin: true, fileName, file, null, Browser, TextWriter.Null);

    [Fact]
    public void A_preset_is_taken_with_what_the_patch_says_about_itself()
    {
        var check = Preset(Files.Patch("A slow drone.", "Ada", "Drone", "ambient"));

        check.Accepted.ShouldBeTrue();
        check.Name.ShouldBe("Drone");
        check.Description.ShouldBe("A slow drone.");
        check.Author.ShouldBe("Ada");
        check.Tags.ShouldBe(["drone", "ambient"]);
        check.Lacks.ShouldBeNull();
    }

    [Fact]
    public void A_name_given_with_the_file_is_the_one_kept_tidied()
    {
        Preset(Files.Patch(), name: "  Night   bus ").Name.ShouldBe("Night bus");
        Preset(Files.Patch(), name: "   ").Name.ShouldBe("Drone");
    }

    [Fact]
    public void A_preset_needing_a_plugin_the_web_pages_lack_says_which()
    {
        var lacks = Preset(Files.Using("example.lantern", "Lantern", "example.lantern.glow"), "Lantern.fbk").Lacks!;

        lacks.Plugins.Select(p => p.Name).ShouldBe(["Lantern"]);
        lacks.Modules.ShouldBe(1);
        lacks.Said.ShouldBe("Needs the Lantern plugin");
    }

    [Fact]
    public void A_preset_needing_several_plugins_the_web_pages_lack_names_them_all()
    {
        var file = Encoding.UTF8.GetBytes("""
            {
              "Requires": [
                { "Id": "example.lantern", "Name": "Lantern" },
                { "Id": "example.kite", "Name": "Kite" },
                { "Id": "example.moth", "Name": "Moth" }
              ],
              "Nodes": [
                { "Id": "8f9d1d3e-0000-4000-8000-000000000021", "TypeId": "example.lantern.glow" },
                { "Id": "8f9d1d3e-0000-4000-8000-000000000022", "TypeId": "example.kite.string" },
                { "Id": "8f9d1d3e-0000-4000-8000-000000000023", "TypeId": "example.moth.wing" }
              ],
              "Connections": []
            }
            """);

        Preset(file, "Night.fbk").Lacks!.Said.ShouldBe("Needs the Lantern, Kite and Moth plugins");
    }

    [Fact]
    public void A_preset_of_modules_the_web_pages_link_lacks_nothing() =>
        Preset(Files.Using("flyback.voice", "Voice", "flyback.voice.bell"), "Bell.fbk").Lacks.ShouldBeNull();

    [Theory]
    [InlineData("notes.fbk", "{\"hello\": 1}")]
    [InlineData("notes.fbk", "not json at all")]
    [InlineData("notes.txt", "{\"Nodes\": [{}]}")]
    [InlineData("broken.fbkb", "not a zip")]
    [InlineData("broken.fbk", """{"Nodes":[null]}""")]
    [InlineData("broken.fbk", """{"Nodes":[{"TypeId":null}]}""")]
    public void A_file_that_is_not_a_patch_is_refused(string fileName, string text)
    {
        var check = Preset(Encoding.UTF8.GetBytes(text), fileName);

        check.Accepted.ShouldBeFalse();
        check.Reason.ShouldBe(PresetCheck.NotAPatch);
    }

    [Fact]
    public void A_bundle_whose_patch_has_a_broken_module_is_refused()
    {
        var bundle = Files.Zip((PatchBundle.PatchEntry, Encoding.UTF8.GetBytes("""{"Nodes":[null]}""")));

        Preset(bundle, "Broken.fbkb").Accepted.ShouldBeFalse();
    }

    [Fact]
    public void A_patch_saved_with_a_byte_order_mark_is_taken() =>
        Preset([0xEF, 0xBB, 0xBF, .. Files.Patch()]).Accepted.ShouldBeTrue();

    [Fact]
    public void A_long_name_is_not_cut_in_the_middle_of_a_character()
    {
        var name = Preset(Files.Patch(), name: new string('a', 59) + "\U0001F3B9").Name!;

        name.ShouldNotContain('�');
        name.Any(char.IsSurrogate).ShouldBeFalse();
    }

    /// <summary>A few megabytes of zeros that inflate past what the site will hold in memory for one file.</summary>
    [Fact]
    public void A_bundle_that_unpacks_past_the_limit_is_refused()
    {
        using var archive = new MemoryStream();

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var patch = zip.CreateEntry(PatchBundle.PatchEntry).Open())
                patch.Write(Files.Patch());

            using var bomb = zip.CreateEntry(PatchBundle.FilesFolder + "silence.wav", CompressionLevel.Fastest).Open();
            var zeros = new byte[1 << 20];

            for (long written = 0; written <= Submissions.BundleLimit; written += zeros.Length) bomb.Write(zeros);
        }

        Preset(archive.ToArray(), "Silence.fbkb").Accepted.ShouldBeFalse();
    }

    [Fact]
    public void A_plugin_is_taken_with_what_its_assembly_says_about_itself()
    {
        var file = Files.Package("win", "linux");
        var check = Plugin(file);

        check.Accepted.ShouldBeTrue();
        check.Assembly.ShouldBe("Flyback.Plugins.Picture");
        check.Adds!.ShouldContain("modules");
        check.Builds.ShouldBe(["win", "linux"]);
        check.Contract!["Flyback.Plugins"].ShouldNotBeNullOrEmpty();
        check.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file)));
        check.FileName.ShouldBe("Flyback.Plugins.Picture.fbkp");
        check.SignerFingerprint.ShouldBe(PackageSigner.Of(Files.Key).Fingerprint);
        check.Signer.ShouldBe(PackageSigner.Of(Files.Key).Key);
        check.Modules!.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_plugins_tags_and_preview_are_read()
    {
        var check = Plugin(Files.Signed(("any/Flyback.Plugins.Sample.dll", Files.SampleAssembly)));

        check.Author.ShouldBe("Flyback");
        check.Tags.ShouldBe(["example", "ripple", "test-fixture"]);
        check.Preview!.Type.ShouldBe("image/png");
        Convert.FromBase64String(check.Preview.Data).Length.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("picture.zip", "a plugin package")]
    [InlineData("picture.fbkp", "no plugin")]
    public void A_file_the_editor_would_refuse_is_refused_saying_why(string fileName, string why)
    {
        var check = Plugin(Files.Signed(("win/readme.txt", [1])), fileName);

        check.Accepted.ShouldBeFalse();
        check.Reason!.ShouldContain(why);
    }

    [Fact]
    public void A_package_that_would_unpack_outside_its_folder_is_refused() =>
        Plugin(Files.Signed(("win/Flyback.Plugins.Picture.dll", Files.PictureAssembly), ("../evil.dll", [1]))).Accepted.ShouldBeFalse();

    /// <summary>The reader asked to check keys, as a Release build always is, so a Debug run holds the rule too.</summary>
    [Fact]
    public void An_unsigned_package_is_refused_where_keys_are_checked() =>
        Should.Throw<InvalidDataException>(() => PluginSubmissions.Read(
                "upload.fbkp", Files.Zip(("win/Flyback.Plugins.Picture.dll", Files.PictureAssembly)), checkKeys: true))
            .Message.ShouldContain("not signed");

    [Fact]
    public void An_unsigned_package_is_refused()
    {
        Assert.SkipUnless(PackageSigner.Checked, "a Debug build takes a package whoever signed it");

        Plugin(Files.Zip(("win/Flyback.Plugins.Picture.dll", Files.PictureAssembly))).Reason!.ShouldContain("not signed");
    }

    [Fact]
    public void The_command_prints_the_verdict_and_says_by_its_exit_code_whether_it_was_taken()
    {
        var taken = Path.Combine(folder.FullName, "Drone.fbk");
        var refused = Path.Combine(folder.FullName, "notes.fbk");
        File.WriteAllBytes(taken, Files.Patch());
        File.WriteAllText(refused, "{}");

        var output = new StringWriter();

        CheckSubmissionCommand.Run(new FileInfo(taken), "Night", Browser, output, TextWriter.Null).ShouldBe(Exit.Ok);
        CheckSubmissionCommand.Run(new FileInfo(refused), null, Browser, output, TextWriter.Null).ShouldBe(Exit.Refused);
        CheckSubmissionCommand.Run(new FileInfo(Path.Combine(folder.FullName, "gone.fbk")), null, Browser, output, TextWriter.Null).ShouldBe(Exit.Failed);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        using (var first = JsonDocument.Parse(lines[0]))
        {
            first.RootElement.GetProperty("accepted").GetBoolean().ShouldBeTrue();
            first.RootElement.GetProperty("name").GetString().ShouldBe("Night");
        }

        using var second = JsonDocument.Parse(lines[1]);
        second.RootElement.GetProperty("reason").GetString().ShouldBe(PresetCheck.NotAPatch);
    }
}

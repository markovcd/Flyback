using System.Text;
using Flyback.Site.Checking;
using Flyback.Site.Commands;
using Flyback.Site.Reading;
using Shouldly;
using Xunit;

namespace Flyback.Site.Tests;

/// <summary>A submitted module pack read as the site reads it, against the modules that ship in the engine.</summary>
public sealed class PackCheckTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-site-pack-");

    public void Dispose() => folder.Delete(recursive: true);

    private const string Lights = """
        pack ada.lights 1.2.0

        /// A slow glow.
        module glow(level = 0.5) = sine(freq: 0.2, amp: level)
        """;

    private static PackCheck Check(string text, string fileName = "upload.fbks") =>
        Checks.OfPack(fileName, Encoding.UTF8.GetBytes(text), TextWriter.Null);

    [Fact]
    public void A_pack_is_taken_with_its_id_version_and_modules()
    {
        var check = Check(Lights);

        check.Accepted.ShouldBeTrue();
        check.Pack.ShouldBe("ada.lights");
        check.Version.ShouldBe("1.2.0");
        check.Modules.ShouldBe([new CheckedModule("ada.lights.glow", "Glow")]);
    }

    [Fact]
    public void A_pack_with_a_mistake_is_refused_on_its_line()
    {
        var check = Check(Lights.Replace("sine(", "sinee("));

        check.Accepted.ShouldBeFalse();
        check.Reason.ShouldStartWith("line 4: ");
    }

    [Fact]
    public void Text_that_is_not_a_pack_is_refused()
    {
        Check("osc = sine(freq: 220)\nout(osc)\n").Accepted.ShouldBeFalse();
        Check(Lights, "lights.txt").Reason.ShouldBe(PackCheck.NotAPack);
        Checks.OfPack("lights.fbks", [0x70, 0x61, 0xff, 0xfe], TextWriter.Null).Reason.ShouldBe(PackCheck.NotAPack);
    }

    [Fact]
    public void A_pack_may_name_only_the_modules_that_ship_in_the_engine()
    {
        var check = Check(Lights.Replace("sine(freq: 0.2, amp: level)", "flyback.effects.chorus(in: level)"));

        check.Accepted.ShouldBeFalse();
    }

    [Fact]
    public void Check_submission_prints_a_packs_verdict()
    {
        var file = new FileInfo(Path.Combine(folder.FullName, "lights.fbks"));
        File.WriteAllText(file.FullName, Lights);
        var output = new StringWriter();

        CheckSubmissionCommand.Run(file, null, BrowserPlugins.Linked(), output, TextWriter.Null).ShouldBe(Exit.Ok);

        output.ToString().ShouldContain("\"pack\":\"ada.lights\"");
    }
}

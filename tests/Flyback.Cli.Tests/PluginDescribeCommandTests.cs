using System.Security.Cryptography;
using System.Text.Json;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Sample;
using Shouldly;
using Xunit;

using Flyback.Cli.Commands;
using Flyback.Cli.Common;

namespace Flyback.Cli.Tests;

/// <summary><c>plugin describe</c>, over packages made here from the sample plugin.</summary>
public sealed class PluginDescribeCommandTests : IDisposable
{
    private readonly string folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-plugin-describe-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private FileInfo File(string name, byte[] bytes)
    {
        var file = new FileInfo(Path.Combine(folder, name));
        System.IO.File.WriteAllBytes(file.FullName, bytes);

        return file;
    }

    /// <summary>The sample plugin packed for any system, signed with <paramref name="key"/> where there is one.</summary>
    private byte[] Package(ECDsa? key = null)
    {
        var build = Directory.CreateDirectory(Path.Combine(folder, "build")).FullName;
        var plugin = typeof(SampleModulesPlugin).Assembly.Location;
        System.IO.File.Copy(plugin, Path.Combine(build, Path.GetFileName(plugin)), overwrite: true);

        var zip = PluginPackage.Pack([(PluginPackage.AnyPlatform, build)]);

        return key is null ? zip : PackageSigner.Sign(zip, key);
    }

    private static (int Code, string Out, string Error) Run(FileInfo file, bool json = false)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var code = PluginDescribeCommand.Run(file, json, output, error);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Json_says_each_build_its_modules_and_what_it_names_with_the_signer_and_the_files_hash()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = Package(key);

        var (code, output, _) = Run(File("sample.fbkp", bytes), json: true);

        code.ShouldBe(Exit.Ok);

        var root = JsonDocument.Parse(output).RootElement;
        root.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        root.GetProperty("signer").GetString().ShouldBe(PackageSigner.Of(key).Fingerprint);
        root.TryGetProperty("refused", out _).ShouldBeFalse();

        var build = root.GetProperty("builds").EnumerateArray().Single();
        build.GetProperty("system").GetString().ShouldBe("any");
        build.GetProperty("adds").EnumerateArray().Select(a => a.GetString()).ShouldContain("modules");
        build.GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("typeId").GetString()).ShouldContain("flyback.sample.ripple");
        build.GetProperty("compiled")[0].GetProperty("references").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString()).ShouldContain("Flyback.Plugins");
    }

    [Fact]
    public void An_unsigned_package_is_described_with_no_signer()
    {
        var (code, output, _) = Run(File("sample.fbkp", Package()));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("modules   Ripple (flyback.sample.ripple)");
        output.ShouldContain("signed    no");
    }

    [Fact]
    public void A_file_that_is_not_a_package_is_refused_and_still_hashed()
    {
        var bytes = "not a zip"u8.ToArray();

        var (code, output, _) = Run(File("text.fbkp", bytes), json: true);

        code.ShouldBe(Exit.Problems);

        var root = JsonDocument.Parse(output).RootElement;
        root.GetProperty("refused").GetString().ShouldBe("It is not a zip file.");
        root.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        root.TryGetProperty("builds", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_file_is_a_failure_not_a_refusal()
    {
        var (code, _, error) = Run(new FileInfo(Path.Combine(folder, "missing.fbkp")));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("missing.fbkp");
    }
}

using System.Security.Cryptography;
using Flyback.Core;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>Makes the key a plugin's packages are signed with (ADR-0132).</summary>
internal static class PluginKeyCommand
{
    public static int Run(FileInfo output, TextWriter writer, TextWriter error)
    {
        var pem = PackageSigner.NewKey();
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };

        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            // Never over an existing key: losing one loses every update to what it signed.
            using var file = new StreamWriter(output.FullName, options);
            file.Write(pem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {output.Name}: {(File.Exists(output.FullName) ? "there is a file there already, and it is left alone." : ex.Message)}");
            return Exit.Failed;
        }

        using var key = ECDsa.Create();
        key.ImportFromPem(pem);

        writer.WriteLine(output.Name);
        writer.WriteLine($"  key       {PackageSigner.Of(key).Fingerprint}");
        writer.WriteLine("  Sign every package of the plugin with it: an update signed with any other key is refused.");

        return Exit.Ok;
    }
}

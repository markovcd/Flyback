using Flyback.Plugins.Hosting;
using Flyback.Site.Reading;

namespace Flyback.Site.Checking;

/// <summary>What check-submission says of a plugin package: refused with a reason, or what its assemblies say about it.</summary>
/// <param name="Signer">The public key that signed it, as the site compares one package's signer with another's.</param>
/// <param name="SignerFingerprint">The same key as the pages show it.</param>
internal sealed record PluginCheck(
    bool Accepted,
    string? Reason = null,
    string? Assembly = null,
    string? Name = null,
    string? Version = null,
    string? Author = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? Adds = null,
    IReadOnlyList<string>? Reaches = null,
    IReadOnlyList<string>? Builds = null,
    IReadOnlyDictionary<string, string>? Contract = null,
    IReadOnlyList<CheckedModule>? Modules = null,
    string? Sha256 = null,
    string? Signer = null,
    string? SignerFingerprint = null,
    CheckedPreview? Preview = null,
    string? FileName = null)
{
    /// <summary>The package read the way the editor reads it, and never run.</summary>
    public static PluginCheck Of(string fileName, byte[] file)
    {
        PluginSubmission read;

        try
        {
            read = PluginSubmissions.Read(fileName, file);
        }
        catch (InvalidDataException refused)
        {
            return new PluginCheck(false, refused.Message);
        }

        return new PluginCheck(
            true,
            null,
            read.Assembly,
            read.Name,
            read.Version,
            read.Author,
            read.Description,
            read.Tags,
            read.Adds,
            read.Reaches,
            read.Builds,
            read.Contract,
            [.. read.Modules.Select(m => new CheckedModule(m.TypeId, m.Name))],
            read.Sha256,
            read.Signer?.Key,
            read.Signer?.Fingerprint,
            read.Preview is { } preview ? new CheckedPreview(preview.MediaType, Convert.ToBase64String(preview.Bytes)) : null,
            read.FileName);
    }
}

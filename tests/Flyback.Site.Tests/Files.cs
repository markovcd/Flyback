using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Picture;

namespace Flyback.Site.Tests;

/// <summary>The files a submission might be: patches, bundles and plugin packages.</summary>
internal static class Files
{
    public static readonly byte[] PictureAssembly = File.ReadAllBytes(typeof(PicturePlugin).Assembly.Location);

    public static readonly byte[] SampleAssembly = File.ReadAllBytes(typeof(Plugins.Sample.SampleModulesPlugin).Assembly.Location);

    public static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static byte[] Patch(string? description = "A slow drone.", string? author = "Ada", params string[] tags)
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.Current);
        patch.Describe(description);
        patch.Credit(author);
        patch.Tag(tags);

        return Encoding.UTF8.GetBytes(PatchIO.ToJson(patch));
    }

    /// <summary>A patch of one module from a plugin, stamped as needing it.</summary>
    public static byte[] Using(string providerId, string providerName, string typeId) => Encoding.UTF8.GetBytes($$"""
        {
          "Requires": [ { "Id": "{{providerId}}", "Name": "{{providerName}}" } ],
          "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000012", "TypeId": "{{typeId}}" } ],
          "Connections": []
        }
        """);

    public static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    /// <summary>A package of <paramref name="entries"/>, signed with <see cref="Key"/>.</summary>
    public static byte[] Signed(params (string Name, byte[] Bytes)[] entries) => PackageSigner.Sign(Zip(entries), Key);

    /// <summary>The picture plugin built for each of <paramref name="platforms"/>.</summary>
    public static byte[] Package(params string[] platforms) =>
        Signed([.. platforms.Select(p => ($"{p}/Flyback.Plugins.Picture.dll", PictureAssembly))]);
}

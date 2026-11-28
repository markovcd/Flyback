using System.Text;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.Site.Checking;

/// <summary>What check-submission says of a module pack: refused with a reason, or its id, version and modules.</summary>
internal sealed record PackCheck(
    bool Accepted,
    string? Reason = null,
    string? Pack = null,
    string? Version = null,
    IReadOnlyList<CheckedModule>? Modules = null)
{
    public const string NotAPack = "That is not a module pack. Send a .fbks file.";

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The pack read against the modules that ship in the engine, the only ones a pack may name.</summary>
    public static PackCheck Of(string fileName, byte[] file) => Of(fileName, file, NodeCatalog.BuiltIn);

    internal static PackCheck Of(string fileName, byte[] file, ModuleCatalog modules)
    {
        if (!Checks.IsPack(fileName)) return new PackCheck(false, NotAPack);
        if (file.Length > InstalledPacks.MostBytes) return new PackCheck(false, $"That pack is over {InstalledPacks.MostBytes / 1024} KB.");

        string text;

        try
        {
            text = Strict.GetString(file);
        }
        catch (DecoderFallbackException)
        {
            return new PackCheck(false, NotAPack);
        }

        var (pack, problems) = InstalledPacks.Read(modules, text.TrimStart('﻿'));

        return pack is null
            ? new PackCheck(false, string.Join("\n", problems.Take(20)))
            : new PackCheck(true, null, pack.Id, pack.Version, [.. pack.Modules.Select(def => new CheckedModule(def.TypeId, def.Name))]);
    }
}

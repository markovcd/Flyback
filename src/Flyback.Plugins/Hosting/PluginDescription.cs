using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// What one assembly says about itself and what its code can reach, read from its
/// metadata without running any of it.
/// </summary>
/// <param name="Metadata">Its <c>AssemblyMetadata</c> pairs, by key.</param>
/// <param name="Adds">The kinds of thing it can register, from the registry methods it calls.</param>
/// <param name="Reaches">What outside Flyback its code names: the network, files, other programs.</param>
/// <param name="Previews">The resources it embeds under a preview's name.</param>
/// <param name="Modules">The modules it declares with <see cref="FlybackModuleAttribute"/>.</param>
internal sealed record AssemblyFacts(
    string Name,
    bool IsPlugin,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> Metadata,
    Version? Version,
    IReadOnlySet<string> Adds,
    IReadOnlySet<string> Reaches,
    IReadOnlyList<AssemblyName> References,
    IReadOnlyList<EmbeddedPreview> Previews,
    IReadOnlyList<DeclaredModule> Modules)
{
    /// <summary>The facts of a managed assembly, or null for anything else — a native library, a text file.</summary>
    public static AssemblyFacts? Of(Stream image)
    {
        try
        {
            using var reader = new PEReader(image, PEStreamOptions.PrefetchEntireImage);

            if (!reader.HasMetadata) return null;

            var metadata = reader.GetMetadataReader();

            if (!metadata.IsAssembly) return null;

            var assembly = metadata.GetAssemblyDefinition();
            var adds = new SortedSet<string>(StringComparer.Ordinal);
            var reaches = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var handle in metadata.TypeReferences)
            {
                var type = metadata.GetTypeReference(handle);

                if (Reach(metadata.GetString(type.Namespace), metadata.GetString(type.Name)) is { } reach) reaches.Add(reach);
            }

            foreach (var handle in metadata.MemberReferences)
            {
                var member = metadata.GetMemberReference(handle);

                if (member.Parent.Kind != HandleKind.TypeReference) continue;

                var parent = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                var (space, type, name) = (metadata.GetString(parent.Namespace), metadata.GetString(parent.Name), metadata.GetString(member.Name));

                if (space == "Flyback.Plugins" && type == "IPluginRegistry" && Registered.TryGetValue(name, out var kind)) adds.Add(kind);

                if (Loads(space, type, name)) reaches.Add(LoadedCode);
            }

            foreach (var handle in metadata.MethodDefinitions)
            {
                if ((metadata.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl) != 0) reaches.Add(NativeCode);
            }

            var (attributes, pairs) = StringAttributes(metadata, assembly);

            return new AssemblyFacts(
                metadata.GetString(assembly.Name),
                ImplementsPlugin(metadata),
                attributes,
                pairs,
                assembly.Version,
                adds,
                reaches,
                [.. metadata.AssemblyReferences.Select(h => metadata.GetAssemblyReference(h)).Select(r =>
                    new AssemblyName { Name = metadata.GetString(r.Name), Version = r.Version })],
                PreviewsOf(reader, metadata),
                ModulesOf(metadata, assembly));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public const string NativeCode = "native code";

    public const string ModulesAdded = "modules";

    public const string LoadedCode = "code it loads while running";

    /// <summary>What each registry method adds, as the dialog says it.</summary>
    private static readonly Dictionary<string, string> Registered = new(StringComparer.Ordinal)
    {
        [nameof(IPluginRegistry.AddModules)] = ModulesAdded,
        [nameof(IPluginRegistry.AddPresets)] = "presets",
        [nameof(IPluginRegistry.AddAudioOutput)] = "a sound output",
        [nameof(IPluginRegistry.AddMidiInput)] = "a MIDI input",
        [nameof(IPluginRegistry.AddPatchAssistant)] = "an assistant",
        [nameof(IPluginRegistry.AddSecretStore)] = "a secret store",
    };

    private static string? Reach(string space, string type) => (space, type) switch
    {
        _ when space == "System.Net" || space.StartsWith("System.Net.", StringComparison.Ordinal) => "the network",
        ("System.IO", "File" or "FileInfo" or "Directory" or "DirectoryInfo" or "FileStream" or "FileSystemWatcher" or "DriveInfo") => "files",
        ("System.Diagnostics", "Process" or "ProcessStartInfo") => "other programs",
        ("Microsoft.Win32", "Registry" or "RegistryKey") => "the registry",
        ("System.Runtime.InteropServices", "NativeLibrary") => NativeCode,
        _ when space == "System.Reflection.Emit" || (space, type) == ("System.Runtime.Loader", "AssemblyLoadContext") => LoadedCode,
        _ => null,
    };

    /// <summary>Loading an assembly, or finding and calling a type or method by name.</summary>
    private static bool Loads(string space, string type, string member) => (space, type) switch
    {
        ("System.Reflection", "Assembly") => member.StartsWith("Load", StringComparison.Ordinal) || member == "UnsafeLoadFrom",
        ("System", "Type") => member is "GetType" or "InvokeMember",
        ("System", "Activator") => member.StartsWith("CreateInstance", StringComparison.Ordinal),
        ("System.Reflection", "MethodBase" or "MethodInfo" or "ConstructorInfo") => member == "Invoke",
        _ => false,
    };

    /// <summary>The contract assembly, which is where <see cref="IFlybackPlugin"/> must come from.</summary>
    public static readonly string Contract = typeof(IFlybackPlugin).Assembly.GetName().Name!;

    /// <summary>Whether it was compiled against <see cref="Contract"/>.</summary>
    public bool ReferencesContract => References.Any(r => string.Equals(r.Name, Contract, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a public, concrete type in it implements <see cref="IFlybackPlugin"/> as
    /// <see cref="Contract"/> defines it, which is what the host looks for. An interface
    /// of that name from anywhere else is not the one.
    /// </summary>
    private static bool ImplementsPlugin(MetadataReader metadata)
    {
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);

            if ((type.Attributes & TypeAttributes.Public) == 0 || (type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0) continue;

            foreach (var implemented in type.GetInterfaceImplementations())
            {
                var face = metadata.GetInterfaceImplementation(implemented).Interface;

                if (face.Kind != HandleKind.TypeReference) continue;

                var reference = metadata.GetTypeReference((TypeReferenceHandle)face);

                if (metadata.GetString(reference.Namespace) == "Flyback.Plugins"
                    && metadata.GetString(reference.Name) == nameof(IFlybackPlugin)
                    && reference.ResolutionScope.Kind == HandleKind.AssemblyReference
                    && string.Equals(
                        metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name),
                        Contract,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The assembly's own string attributes — <c>AssemblyProduct</c>, <c>AssemblyCompany</c> and the rest — by
    /// short name, and its <c>AssemblyMetadata</c> pairs by key.
    /// </summary>
    private static (Dictionary<string, string> Attributes, Dictionary<string, string> Metadata) StringAttributes(
        MetadataReader metadata, AssemblyDefinition assembly)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);

            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;

            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);

            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;

            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);

            if (metadata.GetString(type.Namespace) != "System.Reflection") continue;

            try
            {
                var value = attribute.DecodeValue(StringsOnly.Instance);

                if (value.FixedArguments is [{ Value: string text }])
                    found[ShortName(metadata.GetString(type.Name))] = text;
                else if (value.FixedArguments is [{ Value: string key }, { Value: string pair }]
                    && metadata.GetString(type.Name) == nameof(AssemblyMetadataAttribute))
                    pairs[key] = pair;
            }
            catch (Exception ex) when (ex is BadImageFormatException or NotSupportedException)
            {
                // An attribute whose arguments are not strings is not one of these.
            }
        }

        return (found, pairs);
    }

    /// <summary>The most modules one plugin may declare, for the dialog's sake.</summary>
    public const int ModuleLimit = 1000;

    /// <summary>Its <see cref="FlybackModuleAttribute"/>s, in the order written, each cleaned for showing.</summary>
    private static List<DeclaredModule> ModulesOf(MetadataReader metadata, AssemblyDefinition assembly)
    {
        var found = new List<DeclaredModule>();

        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);

            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;

            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);

            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;

            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);

            if (metadata.GetString(type.Namespace) != "Flyback.Plugins" || metadata.GetString(type.Name) != nameof(FlybackModuleAttribute)) continue;

            try
            {
                if (attribute.DecodeValue(StringsOnly.Instance).FixedArguments is [{ Value: string id }, { Value: string name }]
                    && found.Count < ModuleLimit)
                    found.Add(new DeclaredModule(PluginDescription.Line(id, 128), PluginDescription.Line(name, 64)));
            }
            catch (Exception ex) when (ex is BadImageFormatException or NotSupportedException)
            {
                // Not the attribute's shape, so not a declaration.
            }
        }

        return found;
    }

    /// <summary><c>AssemblyProductAttribute</c> as <c>Product</c>.</summary>
    private static string ShortName(string type)
    {
        var name = type.EndsWith("Attribute", StringComparison.Ordinal) ? type[..^"Attribute".Length] : type;

        return name.StartsWith("Assembly", StringComparison.Ordinal) ? name["Assembly".Length..] : name;
    }

    /// <summary>The resource names a preview is embedded under, and the kind of image each must be.</summary>
    public static readonly IReadOnlyDictionary<string, string> PreviewNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["preview.png"] = "image/png", ["preview.webp"] = "image/webp" };

    /// <summary>The largest preview a plugin may embed.</summary>
    public const int PreviewLimit = 1 << 20;

    private static List<EmbeddedPreview> PreviewsOf(PEReader reader, MetadataReader metadata)
    {
        var found = new List<EmbeddedPreview>();

        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            var name = metadata.GetString(resource.Name);

            if (!resource.Implementation.IsNil || !PreviewNames.ContainsKey(name)) continue;

            var directory = reader.PEHeaders.CorHeader!.ResourcesDirectory;
            var blob = reader.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, directory.Size);

            blob.Offset = (int)Math.Min(resource.Offset, int.MaxValue);

            var length = blob.ReadUInt32();

            // Only the first is read: a second is refused whatever it holds.
            found.Add(length > PreviewLimit || found.Count > 0
                ? new EmbeddedPreview(name, length, null)
                : new EmbeddedPreview(name, length, blob.ReadBytes((int)length)));
        }

        return found;
    }

    /// <summary>Enough of a type provider to decode an attribute whose one argument is a string.</summary>
    private sealed class StringsOnly : ICustomAttributeTypeProvider<object?>
    {
        public static readonly StringsOnly Instance = new();

        public object? GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode;
        public object? GetSystemType() => null;
        public object? GetSZArrayType(object? elementType) => null;
        public object? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;
        public object? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => null;
        public object? GetTypeFromSerializedName(string name) => null;
        public PrimitiveTypeCode GetUnderlyingEnumType(object? type) => throw new NotSupportedException();
        public bool IsSystemType(object? type) => false;
    }
}

/// <summary>A resource an assembly embeds under a preview's name, without its bytes where it is too large to be one.</summary>
internal sealed record EmbeddedPreview(string Name, long Length, byte[]? Bytes);

/// <summary>The one image a plugin shows itself with.</summary>
/// <param name="MediaType"><c>image/png</c> or <c>image/webp</c>.</param>
internal sealed record PluginPreview(string MediaType, byte[] Bytes)
{
    /// <summary>The one preview an assembly embeds, or null for none.</summary>
    /// <exception cref="InvalidDataException">Where there are two, or it is too large, or it is not the image its name says.</exception>
    public static PluginPreview? Of(IReadOnlyList<EmbeddedPreview> previews)
    {
        if (previews.Count == 0) return null;

        if (previews.Count > 1)
            throw new InvalidDataException($"Its plugin assembly embeds {previews.Count} previews, where a plugin has one preview.");

        var (name, length, bytes) = previews[0];

        if (bytes is null)
            throw new InvalidDataException($"Its preview, {name}, is {length >> 10} KB, larger than the {AssemblyFacts.PreviewLimit >> 20} MB a preview may be.");

        var type = AssemblyFacts.PreviewNames[name];

        if (!Looks(type, bytes))
            throw new InvalidDataException($"Its preview, {name}, is not a {(type == "image/png" ? "PNG" : "WebP")} image.");

        return new PluginPreview(type, bytes);
    }

    /// <summary>Whether <paramref name="bytes"/> start the way an image of <paramref name="type"/> does.</summary>
    private static bool Looks(string type, byte[] bytes) => type == "image/png"
        ? bytes.AsSpan().StartsWith(PngSignature)
        : bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}

/// <summary>
/// What a plugin's build says it is and what it can do, as the install dialog shows it:
/// the plugin assembly's own attributes, and what the code of every assembly in the build
/// names. Every string is cleaned for showing.
/// </summary>
/// <param name="Assembly">The plugin assembly's name, which is also the folder it is installed into.</param>
/// <param name="Modules">The modules the plugin assembly declares, which are all it may register.</param>
/// <param name="Tags">Its <c>AssemblyMetadata("Tags", …)</c>, tidied as a patch's tags are.</param>
/// <param name="Adds">What it can register: modules, presets, a sound output and so on.</param>
/// <param name="Reaches">What outside Flyback its code names, from any assembly in the build.</param>
/// <param name="Preview">The image it embeds as <c>preview.png</c> or <c>preview.webp</c>, or null for none.</param>
/// <param name="Compiled">
/// Every assembly in the build and what it was compiled against, the plugin's first.
/// A helper the plugin carries is compiled against the contract as much as the plugin
/// is, and is missed only when it is first called.
/// </param>
internal sealed partial record PluginDescription(
    string Assembly,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Adds,
    IReadOnlyList<string> Reaches,
    PluginPreview? Preview,
    IReadOnlyList<(string Assembly, IReadOnlyList<AssemblyName> References)> Compiled,
    IReadOnlyList<DeclaredModule> Modules)
{
    /// <summary>
    /// Whether it adds modules and was compiled before a plugin declared them, so which
    /// ones cannot be known until it runs.
    /// </summary>
    public bool ModulesUnlisted => Adds.Contains(AssemblyFacts.ModulesAdded) && Modules.Count == 0 && !ModuleDeclarations.Required(Compiled[0].References);

    /// <summary>
    /// Why this build would not be loaded, or null where it would: an assembly compiled
    /// against a contract this Flyback does not offer, or a plugin that knew to declare its
    /// modules adding some without declaring one.
    /// </summary>
    public string? Refusal() =>
        ContractRefusal()
        ?? (Adds.Contains(AssemblyFacts.ModulesAdded) && Modules.Count == 0 && ModuleDeclarations.Required(Compiled[0].References)
            ? "It adds modules without declaring any with [assembly: FlybackModule(id, name)], so Flyback would refuse it."
            : null);

    /// <summary>
    /// The versions of <c>Flyback.Plugins</c> and <c>Flyback.Core</c> the plugin assembly
    /// was compiled against, as the dialog shows them, or an empty string for neither.
    /// </summary>
    public string BuiltAgainst => string.Join(", ", Compiled[0].References
        .Where(r => ContractVersion.IsContract(r.Name))
        .OrderBy(r => r.Name, StringComparer.Ordinal)
        .Select(r => $"{r.Name} {r.Version?.ToString(3)}"));

    /// <summary>
    /// Why no assembly in the build may be loaded by this Flyback, naming it where it is
    /// not the plugin's own, or null where every one may.
    /// </summary>
    public string? ContractRefusal()
    {
        foreach (var (assembly, references) in Compiled)
        {
            if (ContractVersion.Reason(references) is not { } reason) continue;

            return assembly == Assembly
                ? reason
                : $"{assembly}.dll, which the plugin carries, was {char.ToLowerInvariant(reason[0])}{reason[1..]}";
        }

        return null;
    }

    /// <summary>
    /// Describes a build from its files, each by its path inside the build and a way to
    /// open it.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Where the build has more than one plugin assembly at its top, or the plugin's
    /// preview is not one image of the kind and size a preview may be.
    /// </exception>
    /// <returns>Null for a build with no plugin assembly at its top.</returns>
    public static PluginDescription? Of(IEnumerable<(string Path, Func<Stream> Open)> files)
    {
        var entries = new List<AssemblyFacts>();
        var compiled = new List<AssemblyFacts>();
        var adds = new SortedSet<string>(StringComparer.Ordinal);
        var reaches = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, open) in files)
        {
            if (!Binary(path)) continue;

            AssemblyFacts? facts;

            using (var stream = open()) facts = AssemblyFacts.Of(stream);

            if (facts is null)
            {
                reaches.Add(AssemblyFacts.NativeCode);
                continue;
            }

            if (PluginLoadContext.IsHostOwned(facts.Name)) continue;

            adds.UnionWith(facts.Adds);
            reaches.UnionWith(facts.Reaches);
            compiled.Add(facts);

            if (facts.IsPlugin && !path.Contains('/')) entries.Add(facts);
        }

        if (entries.Count == 0) return null;

        if (entries.Count > 1)
            throw new InvalidDataException($"A build holds {entries.Count} plugin assemblies, {string.Join(" and ", entries.Select(e => e.Name))}, where a package has one.");

        var entry = entries[0];

        if (!ValidFolder(entry.Name))
            throw new InvalidDataException($"Its plugin assembly, {Line(entry.Name, 80)}, has a name a folder cannot safely take.");

        return new PluginDescription(
            entry.Name,
            Line(Named(entry, "Product") ?? Named(entry, "Title") ?? entry.Name, 64),
            Line(VersionOf(entry), 32),
            Line(Named(entry, "Company"), 64),
            Paragraph(Named(entry, "Description"), 1000),
            TagsOf(entry),
            [.. adds],
            [.. reaches],
            PluginPreview.Of(entry.Previews),
            [.. compiled.OrderBy(facts => facts == entry ? 0 : 1).Select(facts => (facts.Name, facts.References))],
            entry.Modules);
    }

    /// <summary>The <c>Tags</c> pair split at commas and semicolons.</summary>
    private static List<string> TagsOf(AssemblyFacts facts) =>
        facts.Metadata.TryGetValue("Tags", out var tags)
            ? Patch.TidiedTags(tags.Split([',', ';']).Select(t => Line(t, 64))) ?? []
            : [];

    /// <summary>Describes a plugin folder already on disk.</summary>
    public static PluginDescription? OfFolder(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        try
        {
            return Of(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => (Path.GetRelativePath(folder, file).Replace('\\', '/'), (Func<Stream>)(() => File.OpenRead(file)))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>What may be code: a managed assembly, or a native library for any of the three systems.</summary>
    private static bool Binary(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
        || path.Contains(".so.", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>An attribute's value, or null where it is missing or only the SDK's default, the assembly's own name.</summary>
    private static string? Named(AssemblyFacts facts, string attribute) =>
        facts.Attributes.TryGetValue(attribute, out var value) && value.Length > 0 && value != facts.Name ? value : null;

    /// <summary>The informational version without the commit the SDK appends, else the assembly's version.</summary>
    private static string VersionOf(AssemblyFacts facts) =>
        facts.Attributes.TryGetValue("InformationalVersion", out var informational) && informational.Length > 0
            ? informational.Split('+')[0]
            : facts.Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// Whether <paramref name="name"/> is safe as a folder's name on all three systems:
    /// ASCII, not starting or ending with a dot, and not a name Windows keeps for a device.
    /// </summary>
    public static bool ValidFolder(string name) => FolderPattern().IsMatch(name) && !PluginPackage.Reserved(name);

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$")]
    private static partial Regex FolderPattern();

    /// <summary>One line of text as it may be shown, with nothing in it that draws something other than itself.</summary>
    internal static string Line(string? text, int longest) => Clean(text, longest, lines: false);

    /// <summary>A few paragraphs as they may be shown: <see cref="Line"/>, keeping its line breaks.</summary>
    internal static string Paragraph(string? text, int longest) => Clean(text, longest, lines: true);

    /// <summary>
    /// Drops the characters that change how the rest are drawn — a right-to-left
    /// override that turns <c>exe.txt</c> round, a zero-width joiner — and cuts the
    /// text to <paramref name="longest"/> characters.
    /// </summary>
    private static string Clean(string? text, int longest, bool lines)
    {
        if (text is null) return string.Empty;

        var kept = new StringBuilder();

        foreach (var rune in text.EnumerateRunes())
        {
            if (lines && rune.Value == '\n')
            {
                kept.Append('\n');
                continue;
            }

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator:
                    kept.Append(' ');
                    break;
                case UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned:
                    break;
                default:
                    kept.Append(rune.ToString());
                    break;
            }
        }

        var cleaned = Blank().Replace(kept.ToString(), " ");

        if (lines) cleaned = Gap().Replace(cleaned.Replace(" \n", "\n").Replace("\n ", "\n"), "\n\n");

        cleaned = cleaned.Trim();

        var cut = new StringInfo(cleaned);

        return cut.LengthInTextElements <= longest ? cleaned : cut.SubstringByTextElements(0, longest - 1) + "…";
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex Blank();

    [GeneratedRegex("\n{3,}")]
    private static partial Regex Gap();
}

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// What one assembly says about itself and what its code can reach, read from its
/// metadata without running any of it.
/// </summary>
/// <param name="Adds">The kinds of thing it can register, from the registry methods it calls.</param>
/// <param name="Reaches">What outside Flyback its code names: the network, files, other programs.</param>
internal sealed record AssemblyFacts(
    string Name,
    bool IsPlugin,
    IReadOnlyDictionary<string, string> Attributes,
    Version? Version,
    IReadOnlySet<string> Adds,
    IReadOnlySet<string> Reaches,
    IReadOnlyList<AssemblyName> References)
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

            return new AssemblyFacts(
                metadata.GetString(assembly.Name),
                ImplementsPlugin(metadata),
                StringAttributes(metadata, assembly),
                assembly.Version,
                adds,
                reaches,
                [.. metadata.AssemblyReferences.Select(h => metadata.GetAssemblyReference(h)).Select(r =>
                    new AssemblyName(metadata.GetString(r.Name)) { Version = r.Version })]);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    public const string NativeCode = "native code";

    public const string LoadedCode = "code it loads while running";

    /// <summary>What each registry method adds, as the dialog says it.</summary>
    private static readonly Dictionary<string, string> Registered = new(StringComparer.Ordinal)
    {
        [nameof(IPluginRegistry.AddModules)] = "modules",
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

    /// <summary>Whether a public, concrete type in it implements <see cref="IFlybackPlugin"/>, which is what the host looks for.</summary>
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

                if (metadata.GetString(reference.Namespace) == "Flyback.Plugins" && metadata.GetString(reference.Name) == nameof(IFlybackPlugin))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The assembly's own string attributes — <c>AssemblyProduct</c>, <c>AssemblyCompany</c> and the rest — by short name.</summary>
    private static Dictionary<string, string> StringAttributes(MetadataReader metadata, AssemblyDefinition assembly)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

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
                    found[metadata.GetString(type.Name).Replace("Attribute", "", StringComparison.Ordinal)] = text;
            }
            catch (Exception ex) when (ex is BadImageFormatException or NotSupportedException)
            {
                // An attribute whose arguments are not a string is not one of these.
            }
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

/// <summary>
/// What a plugin's build says it is and what it can do, as the install dialog shows it:
/// the plugin assembly's own attributes, and what the code of every assembly in the build
/// names. Every string is cleaned for showing.
/// </summary>
/// <param name="Assembly">The plugin assembly's name, which is also the folder it is installed into.</param>
/// <param name="Adds">What it can register: modules, presets, a sound output and so on.</param>
/// <param name="Reaches">What outside Flyback its code names, from any assembly in the build.</param>
/// <param name="References">What the plugin assembly was compiled against, for the contract check.</param>
internal sealed partial record PluginDescription(
    string Assembly,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<string> Adds,
    IReadOnlyList<string> Reaches,
    IReadOnlyList<AssemblyName> References)
{
    /// <summary>
    /// Describes a build from its files, each by its path inside the build and a way to
    /// open it.
    /// </summary>
    /// <exception cref="InvalidDataException">Where the build has more than one plugin assembly at its top.</exception>
    /// <returns>Null for a build with no plugin assembly at its top.</returns>
    public static PluginDescription? Of(IEnumerable<(string Path, Func<Stream> Open)> files)
    {
        var entries = new List<AssemblyFacts>();
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
            [.. adds],
            [.. reaches],
            entry.References);
    }

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

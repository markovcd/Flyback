using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
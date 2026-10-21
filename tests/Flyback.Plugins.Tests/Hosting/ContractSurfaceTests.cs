using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>
/// The contract is what a plugin may name, so a public type no plugin built here
/// names is either the host's own business, and goes internal, or a promise to
/// plugins not in this repository, and is listed in <see cref="Promised"/> with the
/// reason it is kept.
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
public class ContractSurfaceTests
{
    /// <summary>
    /// Public types no plugin in the box names, kept for plugins elsewhere. Each
    /// entry's reason has to hold on its own; "a plugin might" is not one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Promised = new Dictionary<string, string>
    {
        ["Flyback.Core.Compile.IImageLibrary"] = "An extra that shows a file reads it through the library the compiler hands it, as the built-in Picture extra does.",
        ["Flyback.Core.Compile.LoadedMidi"] = "What ISampleLibrary.FindMidi answers.",
        ["Flyback.Core.Compile.MidiLine"] = "What LoadedMidi.Line answers.",
        ["Flyback.Core.Compile.MidiNote"] = "What LoadedMidi.Notes holds.",
        ["Flyback.Core.Graph.NodeGroup"] = "Patch.Groups, saved in the patch file.",
        ["Flyback.Core.Graph.GroupSocket"] = "NodeGroup.Exposed, saved in the patch file.",
        ["Flyback.Core.Graph.MidiBinding"] = "PatchControl.Midi, saved in the patch file.",
        ["Flyback.Core.Graph.ControlKind"] = "PatchControl.Kind, saved in the patch file.",
        ["Flyback.Core.Graph.Connection"] = "Patch.Connections: a plugin that reads a patch rather than building one walks its wires.",
        ["Flyback.Core.Graph.KeyboardScale"] = "Patch.Keyboard, saved in the patch file.",
        ["Flyback.Core.Graph.ScaleMode"] = "KeyboardScale.Mode's type.",
        ["Flyback.Core.Graph.StepSpec"] = "The Steps extra's field.",
        ["Flyback.Core.Graph.PatchBuilder"] = "How a plugin builds a preset in code rather than in text.",
        ["Flyback.Core.Graph.PatchControl"] = "Patch.AddControl's answer, a panel knob of a preset built in code.",
        ["Flyback.Core.Graph.ControlMap"] = "How a preset built in code links a panel knob to a socket.",
        ["Flyback.Core.Graph.ControlLink"] = "ControlMap.Link's answer, saved in a module's state.",
        ["Flyback.Core.Graph.Step"] = "One step of a sequencer a preset built in code fills in.",
        ["Flyback.Core.Graph.PartLevel"] = "ArrangementExtra's default, one part's level in one section.",
        ["Flyback.Plugins.Midi.MidiAction"] = "MidiMessage.Action's type, which a MIDI input plugin builds a message with.",
        ["Flyback.Plugins.Decide.IPreparedModel"] = "The only way a decision model that runs here gets its files: the host downloads them, so the plugin never reaches the network (ADR-0186).",
        ["Flyback.Plugins.Decide.ModelFile"] = "What IPreparedModel.Needs lists.",
        ["Flyback.Plugins.Assist.Listener"] = "AssistantSenses.Hearing's type, named by an assistant that reports its senses without AssistantSchema.",
        ["Flyback.Plugins.Settings.SettingField+Switch"] = "A field kind a plugin's settings form is built from; the forms in the box are AssistantSchema's.",
    };

    private static readonly Assembly[] Contract = [typeof(NodeDef).Assembly, typeof(IFlybackPlugin).Assembly];

    [Fact]
    public void Every_public_type_is_named_by_a_plugin_or_promised_with_a_reason()
    {
        var named = NamedByPlugins();

        var unaccounted = Contract
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => !DeclaresAnInput(t))
            .Select(t => t.FullName!)
            .Where(name => !named.Contains(name) && !Promised.ContainsKey(name))
            .Order()
            .ToList();

        unaccounted.ShouldBeEmpty(
            "No plugin built here names these. Make each internal, or add it to Promised with the reason a plugin elsewhere needs it:\n  "
            + string.Join("\n  ", unaccounted));
    }

    [Fact]
    public void A_promise_is_kept_only_while_no_plugin_here_names_the_type()
    {
        var named = NamedByPlugins();
        var exported = Contract.SelectMany(a => a.GetExportedTypes()).Select(t => t.FullName!).ToHashSet();

        Promised.Keys.Where(named.Contains).ShouldBeEmpty("A plugin names these now, so they leave Promised.");
        Promised.Keys.Where(name => !exported.Contains(name)).ShouldBeEmpty("These are no longer public.");
    }

    [Fact]
    public void A_member_promise_is_kept_only_while_no_plugin_here_names_the_member()
    {
        var named = MembersNamedByPlugins();
        var exported = Contract.SelectMany(a => a.GetExportedTypes()).Select(t => t.FullName!).ToHashSet();

        PromisedMembers.Keys.Where(named.Contains).ShouldBeEmpty("A plugin names these now, so they leave PromisedMembers.");
        SavedData.Keys.Where(name => !exported.Contains(name)).ShouldBeEmpty("These are no longer public.");
    }

    [Fact]
    public void Every_public_member_is_named_by_a_plugin_or_promised_with_a_reason()
    {
        var named = MembersNamedByPlugins();

        var unaccounted = Contract
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => !Promised.ContainsKey(t.FullName!) && !DeclaresAnInput(t) && !t.IsInterface && !t.IsEnum && !t.IsSubclassOf(typeof(Delegate)))
            .SelectMany(Answerable)
            .Where(member => !named.Contains(member) && !PromisedMembers.ContainsKey(member))
            .Distinct()
            .Order()
            .ToList();

        unaccounted.ShouldBeEmpty(
            "No plugin built here names these. Make each internal, or add it to PromisedMembers with the reason a plugin elsewhere needs it:\n  "
            + string.Join("\n  ", unaccounted));
    }

    [Fact]
    public void Every_extra_in_core_is_public()
    {
        typeof(NodeDef).Assembly.GetTypes()
            .Where(t => DeclaresAnInput(t) && !t.IsVisible)
            .Select(t => t.FullName!)
            .Order()
            .ShouldBeEmpty("An extra or a field kind is how a plugin's module declares an input that is not a knob, so it stays public with all its members, whether or not a plugin built here declares one:");
    }

    /// <summary>
    /// An extra or a field kind: what a plugin's module declares its inputs with
    /// (a scale, a sample, a MIDI channel, a choice), so it and its members are the
    /// contract even when no plugin built here declares one.
    /// </summary>
    private static bool DeclaresAnInput(Type type) =>
        type.IsSubclassOf(typeof(NodeExtra)) || type.IsSubclassOf(typeof(ExtraField));

    /// <summary>
    /// Every contract type the plugins built beside this test name, as reflection
    /// names it, nested types as <c>Outer+Inner</c>: what their IL references, and
    /// what their sources reach a member through, since a constant is inlined and
    /// leaves no reference behind.
    /// </summary>
    private static HashSet<string> NamedByPlugins()
    {
        var names = ReferencedByPluginIl();
        names.UnionWith(ReachedInPluginSources());
        return names;
    }

    private static HashSet<string> ReferencedByPluginIl()
    {
        var names = new HashSet<string>();
        var folder = Path.Combine(AppContext.BaseDirectory, "plugins");

        foreach (var dll in Directory.EnumerateFiles(folder, "Flyback.Plugins.*.dll", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(dll);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();

            foreach (var handle in metadata.TypeReferences)
            {
                var name = ContractTypeName(metadata, metadata.GetTypeReference(handle));
                if (name is not null)
                    names.Add(name);
            }
        }

        names.ShouldNotBeEmpty($"No plugin was found under {folder}.");
        return names;
    }

    /// <summary>
    /// A type's simple name followed by a member access, outside comments. Two
    /// types sharing a simple name are both counted as named, which errs toward
    /// keeping a type public.
    /// </summary>
    private static HashSet<string> ReachedInPluginSources()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "PluginSources");
        var bySimpleName = Contract
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => !t.IsNested)
            .ToLookup(t => t.Name, t => t.FullName!);

        var names = new HashSet<string>();
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).ToList();
        files.ShouldNotBeEmpty($"No plugin source was found under {folder}.");

        foreach (var file in files)
        {
            var code = Comments.Replace(File.ReadAllText(file), string.Empty);
            foreach (Match access in MemberAccess.Matches(code))
                names.UnionWith(bySimpleName[access.Groups[1].Value]);
        }

        return names;
    }

    /// <summary>
    /// Public members of a named type that no plugin in the box names, kept for
    /// plugins elsewhere, as <c>Type.Member</c>. Each reason has to hold on its own.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PromisedMembers = new Dictionary<string, string>
    {
        ["Flyback.Core.Graph.NodeCatalog.AdsrTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.ArrangementTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.BeamTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.CloudsTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.CoordTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.DelayTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.DeskTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.DriveTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.ExpressionTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.FilterTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.NoiseTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.OutputTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.ReverbTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.SineTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.SlewTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.TimeTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.TriangleTypeId"] = "A built-in module a preset built with PatchBuilder adds.",
        ["Flyback.Core.Graph.NodeCatalog.CoordYPort"] = "The Coord socket a preset built with PatchBuilder wires for y.",
        ["Flyback.Core.Graph.NodeCatalog.OutputColorPort"] = "The Output socket a preset built with PatchBuilder wires the picture to.",
        ["Flyback.Core.Graph.NodeCatalog.OutputLeftPort"] = "The Output socket a preset built with PatchBuilder wires the left ear to.",
        ["Flyback.Core.Graph.NodeCatalog.OutputRightPort"] = "The Output socket a preset built with PatchBuilder wires the right ear to.",
        ["Flyback.Core.Graph.NodeCatalog.OutputVolumePort"] = "The Output socket a preset built with PatchBuilder sets the volume on.",
        ["Flyback.Core.Graph.NodeCatalog.MaxParts"] = "How many parts an ArrangementExtra a plugin declares may have.",
        ["Flyback.Core.Graph.Patch.AddControl"] = "How a preset built in code puts a knob on its panel.",
        ["Flyback.Core.Graph.Patch.Describe"] = "How a preset built in code says what it is.",
    };

    /// <summary>
    /// Types a serializer writes, whose public properties, and the constructor it
    /// builds them with, are the saved shape however few plugins read them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SavedData = new Dictionary<string, string>
    {
        ["Flyback.Core.Graph.Patch"] = "The patch file.",
        ["Flyback.Core.Graph.NodeInstance"] = "Patch.Nodes, saved in the patch file.",
        ["Flyback.Core.Graph.PatchControl"] = "Patch.Controls, saved in the patch file.",
        ["Flyback.Core.Graph.KeyboardScale"] = "Patch.Keyboard, saved in the patch file.",
        ["Flyback.Core.Graph.ControlLink"] = "Saved in a module's state by ControlMap.",
        ["Flyback.Plugins.Assist.ModelReport"] = "What Survey keeps in the settings file.",
    };

    /// <summary>
    /// The members of <paramref name="type"/> a plugin reaches only by naming them,
    /// as <c>Type.Member</c>: not what a plugin implements or overrides, not what a
    /// record writes for itself or a positional record's shape, and not what a
    /// serializer writes of a <see cref="SavedData"/> type.
    /// </summary>
    private static IEnumerable<string> Answerable(Type type)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var implementing = type.GetInterfaces().SelectMany(i => type.GetInterfaceMap(i).TargetMethods).ToHashSet();
        var shape = RecordShape(type);
        var saved = SavedData.ContainsKey(type.FullName!);

        bool Sealed(MethodInfo? method) =>
            method is null
            || !method.IsAbstract && (!method.IsVirtual || method.IsFinal) && method.GetBaseDefinition() == method && !implementing.Contains(method);

        bool Written(PropertyInfo property) =>
            saved && property.GetMethod is { IsStatic: false }
            && property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition is not JsonIgnoreCondition.Always;

        bool Own(MemberInfo member) => !RecordWrites.Contains(member.Name) && member switch
        {
            ConstructorInfo constructor => !Positional(constructor, shape) && !(saved && constructor.GetParameters().Length == 0),
            MethodInfo method => (!method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal)) && Sealed(method),
            PropertyInfo property => !shape.Contains(property.Name) && !Written(property) && property.GetAccessors().All(Sealed),
            EventInfo e => Sealed(e.AddMethod),
            FieldInfo => true,
            _ => false,
        };

        return type.GetMembers(declared).Where(Own).Select(member => Key(type, member is ConstructorInfo ? ".ctor" : member.Name));
    }

    /// <summary>
    /// The parameters of a positional record's primary constructor, which with the
    /// properties they declare are the record itself rather than members added to
    /// it; empty for anything else.
    /// </summary>
    private static HashSet<string> RecordShape(Type type)
    {
        var deconstruct = type.GetMethod("Deconstruct", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (deconstruct is null || type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is null)
            return [];

        return [.. deconstruct.GetParameters().Select(p => p.Name!)];
    }

    private static bool Positional(ConstructorInfo constructor, HashSet<string> shape) =>
        shape.Count > 0 && constructor.GetParameters().Select(p => p.Name!).ToHashSet().SetEquals(shape);

    private static readonly HashSet<string> RecordWrites =
        ["<Clone>$", "Deconstruct", "Equals", "GetHashCode", "ToString", "PrintMembers", "EqualityContract", "op_Equality", "op_Inequality"];

    private static string Key(Type type, string member) => type.FullName + "." + member;

    /// <summary>
    /// Every contract member the plugins built beside this test name, as
    /// <c>Type.Member</c>: what their IL references, an accessor counting as its
    /// property, what their sources reach as <c>Type.Member</c>, since a constant
    /// leaves no reference, and the properties a named constructor sets.
    /// </summary>
    private static HashSet<string> MembersNamedByPlugins()
    {
        var names = new HashSet<string>();
        var folder = Path.Combine(AppContext.BaseDirectory, "plugins");

        foreach (var dll in Directory.EnumerateFiles(folder, "Flyback.Plugins.*.dll", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(dll);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();

            foreach (var handle in metadata.MemberReferences)
            {
                var reference = metadata.GetMemberReference(handle);
                if (ContractTypeName(metadata, reference.Parent) is not { } owner) continue;

                var name = metadata.GetString(reference.Name);
                names.Add(owner + "." + name);
                if (Accessor.Match(name) is { Success: true } accessor) names.Add(owner + "." + accessor.Groups[1].Value);
            }
        }

        var exported = Contract.SelectMany(a => a.GetExportedTypes()).ToList();
        var code = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "PluginSources"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Comments.Replace(File.ReadAllText(file), string.Empty)));

        foreach (Match access in StaticAccess.Matches(code))
            foreach (var type in exported.Where(t => t.Name == access.Groups[1].Value))
                names.Add(Key(type, access.Groups[2].Value));

        foreach (var type in exported.Where(t => names.Contains(Key(t, ".ctor"))))
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
                foreach (var property in type.GetProperties().Where(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
                    names.Add(Key(type, property.Name));

        return names;
    }

    private static readonly Regex Accessor = new(@"^(?:get_|set_|add_|remove_)(.+)$", RegexOptions.Compiled);

    private static readonly Regex StaticAccess = new(@"\b([A-Z][A-Za-z0-9_]*)(?=\.([A-Za-z_][A-Za-z0-9_]*))", RegexOptions.Compiled);

    /// <summary>The contract type a member reference hangs off, as reflection names it, or null for anything else.</summary>
    private static string? ContractTypeName(MetadataReader metadata, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return ContractTypeName(metadata, metadata.GetTypeReference((TypeReferenceHandle)parent));

            case HandleKind.TypeSpecification:
                var blob = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return null;
                blob.ReadSignatureTypeCode();
                return ContractTypeName(metadata, blob.ReadTypeHandle());

            default:
                return null;
        }
    }

    private static readonly Regex Comments = new(@"//[^\r\n]*", RegexOptions.Compiled);

    private static readonly Regex MemberAccess = new(@"\b([A-Z][A-Za-z0-9_]*)\.[A-Za-z_]", RegexOptions.Compiled);

    private static string? ContractTypeName(MetadataReader metadata, TypeReference reference)
    {
        var name = metadata.GetString(reference.Name);

        switch (reference.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                var assembly = metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope);
                if (metadata.GetString(assembly.Name) is not ("Flyback.Core" or "Flyback.Plugins"))
                    return null;
                var ns = metadata.GetString(reference.Namespace);
                return ns.Length == 0 ? name : ns + "." + name;

            case HandleKind.TypeReference:
                var outer = ContractTypeName(metadata, metadata.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope));
                return outer is null ? null : outer + "+" + name;

            default:
                return null;
        }
    }
}

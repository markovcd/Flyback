using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The contract is what a plugin may name, so a public type no plugin built here
/// names is either the host's own business, and goes internal, or a promise to
/// plugins not in this repository, and is listed in <see cref="Promised"/> with the
/// reason it is kept.
/// </summary>
public class ContractSurfaceTests
{
    /// <summary>
    /// Public types no plugin in the box names, kept for plugins elsewhere. Each
    /// entry's reason has to hold on its own; "a plugin might" is not one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Promised = new Dictionary<string, string>
    {
        ["Flyback.Core.Compile.ISampleLibrary"] = "An extra that plays a file reads it through the library the compiler hands it, as the built-in Sample extra does.",
        ["Flyback.Core.Compile.IImageLibrary"] = "An extra that shows a file reads it through the library the compiler hands it, as the built-in Picture extra does.",
        ["Flyback.Core.Compile.LoadedSample"] = "What ISampleLibrary.Find answers.",
        ["Flyback.Core.Compile.LoadedMidi"] = "What ISampleLibrary.FindMidi answers.",
        ["Flyback.Core.Compile.MidiLine"] = "What LoadedMidi.Line answers.",
        ["Flyback.Core.Compile.MidiNote"] = "What LoadedMidi.Notes holds.",
        ["Flyback.Core.Graph.NodeGroup"] = "Patch.Groups, saved in the patch file.",
        ["Flyback.Core.Graph.GroupSocket"] = "NodeGroup.Exposed, saved in the patch file.",
        ["Flyback.Core.Graph.MidiBinding"] = "PatchControl.Midi, saved in the patch file.",
        ["Flyback.Core.Graph.Connection"] = "Patch.Connections: a plugin that reads a patch rather than building one walks its wires.",
        ["Flyback.Core.Graph.ScaleMode"] = "KeyboardScale.Mode's type.",
        ["Flyback.Core.Graph.StepSpec"] = "The Steps extra's field.",
        ["Flyback.Core.Graph.ExtraField+Toggle"] = "One of the field kinds an extra declares its editor with (ADR-0055); the extras in the box need no switch.",
        ["Flyback.Core.Graph.Extras.FormulaExtra"] = "A plugin's preset configures a built-in module through its extra, as the presets in the box do through SampleExtra.",
        ["Flyback.Core.Graph.Extras.MidiClockExtra"] = "A plugin's preset configures a built-in module through its extra, as the presets in the box do through SampleExtra.",
        ["Flyback.Core.Graph.Extras.MidiFileExtra"] = "A plugin's preset configures a built-in module through its extra, as the presets in the box do through SampleExtra.",
        ["Flyback.Core.Graph.Extras.MidiLineExtra"] = "A plugin's preset configures a built-in module through its extra, as the presets in the box do through SampleExtra.",
        ["Flyback.Core.Graph.Extras.PictureExtra"] = "A plugin's preset configures a built-in module through its extra, as the presets in the box do through SampleExtra.",
        ["Flyback.Plugins.Midi.MidiAction"] = "MidiMessage.Action's type, which a MIDI input plugin builds a message with.",
        ["Flyback.Plugins.Assist.Listener"] = "AssistantSenses.Hearing's type, named by an assistant that reports its senses without AssistantSchema.",
        ["Flyback.Plugins.Settings.SettingField+Switch"] = "A field kind a plugin's settings form is built from; the forms in the box are AssistantSchema's.",
        ["Flyback.Plugins.Settings.SettingField+Text"] = "A field kind a plugin's settings form is built from; the forms in the box are AssistantSchema's.",
    };

    private static readonly Assembly[] Contract = [typeof(NodeDef).Assembly, typeof(IFlybackPlugin).Assembly];

    [Fact]
    public void Every_public_type_is_named_by_a_plugin_or_promised_with_a_reason()
    {
        var named = NamedByPlugins();

        var unaccounted = Contract
            .SelectMany(a => a.GetExportedTypes())
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

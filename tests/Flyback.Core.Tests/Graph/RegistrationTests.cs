using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The catalog's modules and the shipped presets are listed by hand, so one
/// written and left off its list compiles and ships nothing.
/// </summary>
public class RegistrationTests
{
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static TheoryData<string> ModuleMethods => [.. Methods(typeof(NodeCatalog), DefinesModules)];

    public static TheoryData<string> PresetBuilders => [.. Methods(typeof(Presets), BuildsAPatch)];

    /// <summary>
    /// Every parameterless method on <see cref="NodeCatalog"/> that makes a module
    /// or a group of them has each of its modules in <see cref="NodeCatalog.BuiltIn"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleMethods))]
    public void Every_module_the_catalog_defines_is_in_it(string method)
    {
        var listed = NodeCatalog.BuiltIn.All.Select(d => d.TypeId).ToHashSet();

        var made = typeof(NodeCatalog).GetMethod(method, Statics, Type.EmptyTypes)!.Invoke(null, null) switch
        {
            NodeDef one => [one],
            IEnumerable<NodeDef> many => many,
            _ => [],
        };

        made.Select(d => d.TypeId).Where(id => !listed.Contains(id)).ShouldBeEmpty(
            $"NodeCatalog.{method}() makes modules the static constructor never adds");
    }

    /// <summary>Every <c>Patch X(ModuleCatalog)</c> on <see cref="Presets"/> is the build of a shipped preset.</summary>
    [Theory]
    [MemberData(nameof(PresetBuilders))]
    public void Every_preset_written_is_shipped(string builder) =>
        Presets.Shipped.Select(p => p.Builder.Method.Name).ShouldContain(builder,
            $"Presets.{builder} builds a patch that Presets.Shipped never lists");

    private static IEnumerable<string> Methods(Type type, Func<MethodInfo, bool> keep) => type
        .GetMethods(Statics)
        .Where(m => !m.IsSpecialName && !m.IsGenericMethod && !m.Name.Contains('<') && keep(m))
        .Select(m => m.Name);

    private static bool DefinesModules(MethodInfo m) =>
        m.GetParameters().Length == 0
        && (m.ReturnType == typeof(NodeDef) || m.ReturnType == typeof(IEnumerable<NodeDef>));

    private static bool BuildsAPatch(MethodInfo m) =>
        m.ReturnType == typeof(Patch)
        && m.GetParameters() is [{ ParameterType: var only }]
        && only == typeof(ModuleCatalog);
}

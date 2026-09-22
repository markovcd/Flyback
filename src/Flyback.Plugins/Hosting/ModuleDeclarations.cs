using System.Reflection;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Hosting;

/// <summary>A module a plugin declares with <see cref="FlybackModuleAttribute"/>.</summary>
internal sealed record DeclaredModule(string TypeId, string Name);

/// <summary>Holds a plugin to the modules it declares (<see cref="FlybackModuleAttribute"/>).</summary>
internal static class ModuleDeclarations
{
    /// <summary>The contract version that brought <see cref="FlybackModuleAttribute"/>.</summary>
    public static Version Since { get; } = new(1, 2);

    /// <summary>Whether an assembly compiled against <paramref name="references"/> knew to declare its modules.</summary>
    public static bool Required(IEnumerable<AssemblyName> references) => references.Any(r =>
        string.Equals(r.Name, AssemblyFacts.Contract, StringComparison.OrdinalIgnoreCase) && r.Version is { } version && version >= Since);

    public static IReadOnlyList<DeclaredModule> Of(Assembly assembly) =>
        [.. assembly.GetCustomAttributes<FlybackModuleAttribute>().Select(a => new DeclaredModule(a.Id, a.Name))];

    /// <summary>
    /// Why the modules registered are not the ones declared, or null where every one
    /// registered is declared, under the same name.
    /// </summary>
    public static string? Mismatch(IReadOnlyList<DeclaredModule> declared, IEnumerable<NodeDef> registered)
    {
        var wrong = new List<string>();

        foreach (var module in registered)
        {
            if (declared.FirstOrDefault(d => d.TypeId == module.TypeId) is not { } match)
                wrong.Add($"'{module.TypeId}' is not declared");
            else if (match.Name != module.Name)
                wrong.Add($"'{module.TypeId}' is declared as \"{match.Name}\" and registered as \"{module.Name}\"");
        }

        if (wrong.Count == 0) return null;

        var named = string.Join("; ", wrong.Take(3)) + (wrong.Count > 3 ? $"; and {wrong.Count - 3} more" : "");

        return $"refused — it registers modules it does not declare: {named}. "
            + "Each needs [assembly: FlybackModule(id, name)].";
    }
}

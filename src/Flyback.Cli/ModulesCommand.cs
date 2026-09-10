using System.Text.Json;
using Flyback.Core.Graph;

namespace Flyback.Cli;

/// <summary>One module of the installed catalogue, as this command writes it out.</summary>
internal sealed record Module(
    string TypeId,
    string Name,
    string Category,
    string Provider,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs);

/// <summary>
/// Says what modules this build has.
/// </summary>
/// <remarks>
/// The question a patch that did not load completely raises and nothing else
/// answers: <c>info</c> says what a patch requires, and this says what is here
/// to meet it. It matters most to this program, which reads the plugins beside
/// it and has none when it is run out of a build rather than a publish.
/// </remarks>
internal static class ModulesCommand
{
    public static int Run(ModuleCatalog catalog, bool json, TextWriter output)
    {
        var modules = catalog.All
            .Select(def => new Module(
                def.TypeId,
                def.Name,
                def.Category,
                (catalog.ProviderOf(def.TypeId) ?? NodeCatalog.BuiltInProvider).Id,
                [.. def.Inputs.Select(port => port.Name)],
                [.. def.Outputs.Select(port => port.Name)]))
            .ToArray();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    providers = catalog.Providers.Select(provider => new { provider.Id, provider.Name }),
                    modules,
                },
                Writing.Json));

            return Exit.Ok;
        }

        Write(catalog, modules, output);

        return Exit.Ok;
    }

    private static void Write(ModuleCatalog catalog, IReadOnlyList<Module> modules, TextWriter output)
    {
        output.WriteLine("providers");

        foreach (var provider in catalog.Providers)
        {
            var count = modules.Count(module => module.Provider == provider.Id);

            output.WriteLine($"  {provider.Id,-20} {provider.Name,-20} {Writing.Count(count, "module")}");
        }

        // By category and in the catalogue's own order, which is the order the
        // palette shows them in.
        foreach (var category in catalog.Categories)
        {
            output.WriteLine();
            output.WriteLine(category);

            foreach (var module in modules.Where(module => module.Category == category))
                output.WriteLine($"  {module.TypeId,-24} {module.Name}");
        }
    }
}

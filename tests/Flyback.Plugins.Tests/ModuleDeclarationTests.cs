using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

[assembly: Flyback.Plugins.FlybackModule("flyback.honest.tone", "Tone")]
[assembly: Flyback.Plugins.FlybackModule("flyback.honest.spare", "Spare")]
[assembly: Flyback.Plugins.FlybackModule("flyback.misnamed.tone", "Tone")]

namespace Flyback.Plugins.Tests;

/// <summary>
/// A plugin declares its modules with <see cref="FlybackModuleAttribute"/>, and the host
/// keeps nothing from one that registers a module it did not declare.
/// </summary>
/// <remarks>
/// The plugins below live in this assembly, which is compiled against the current
/// contract and carries the declarations above, so each is held to them.
/// </remarks>
public class ModuleDeclarationTests
{
    private static NodeDef Module(string id, string name) =>
        new(id, name, "Test", [new PortSpec("in")], [new PortSpec("out")], (em, i) => [em.Mul(i[0], 0.5f)]);

    private sealed class Honest : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("honest", "Honest");

        public void Register(IPluginRegistry registry) =>
            registry.AddModules(new ModuleProvider("flyback.honest", "Honest"), [Module("flyback.honest.tone", "Tone")]);
    }

    private sealed class Undeclared : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("undeclared", "Undeclared");

        public void Register(IPluginRegistry registry)
        {
            registry.AddPresets([new PatchPreset("Undeclared's preset", _ => new Patch())]);
            registry.AddModules(new ModuleProvider("flyback.undeclared", "Undeclared"), [Module("flyback.undeclared.hidden", "Hidden")]);
        }
    }

    private sealed class Misnamed : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("misnamed", "Misnamed");

        public void Register(IPluginRegistry registry) =>
            registry.AddModules(new ModuleProvider("flyback.misnamed", "Misnamed"), [Module("flyback.misnamed.tone", "Tune")]);
    }

    [Fact]
    public void A_plugin_registering_only_what_it_declares_loads_and_may_leave_a_declared_module_out()
    {
        var catalog = PluginHost.LoadTypes(typeof(Honest));

        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Select(p => p.Info.Id).ShouldBe(["honest"]);
        catalog.Modules.Get("flyback.honest.tone").ShouldNotBeNull();
    }

    [Fact]
    public void A_plugin_registering_a_module_it_did_not_declare_is_refused_whole()
    {
        var catalog = PluginHost.LoadTypes(typeof(Honest), typeof(Undeclared));

        catalog.Plugins.Select(p => p.Info.Id).ShouldBe(["honest"]);
        catalog.Modules.Get("flyback.undeclared.hidden").ShouldBeNull();
        catalog.Modules.Providers.Select(p => p.Id).ShouldNotContain("flyback.undeclared");
        catalog.Presets.Select(p => p.Name).ShouldNotContain("Undeclared's preset");
        catalog.Problems.ShouldHaveSingleItem().Message.ShouldContain("'flyback.undeclared.hidden' is not declared");
        catalog.Modules.Get("flyback.honest.tone").ShouldNotBeNull();
    }

    private sealed class Thrower : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("thrower", "Thrower");

        public void Register(IPluginRegistry registry)
        {
            registry.AddModules(new ModuleProvider("flyback.thrower", "Thrower"), [Module("flyback.thrower.hidden", "Hidden")]);
            throw new InvalidOperationException("gone wrong");
        }
    }

    [Fact]
    public void A_plugin_that_throws_while_registering_leaves_nothing_behind()
    {
        var catalog = PluginHost.LoadTypes(typeof(Honest), typeof(Thrower));

        catalog.Plugins.Select(p => p.Info.Id).ShouldBe(["honest"]);
        catalog.Modules.Get("flyback.thrower.hidden").ShouldBeNull();
        catalog.Modules.Providers.Select(p => p.Id).ShouldNotContain("flyback.thrower");
        catalog.Problems.ShouldHaveSingleItem().Message.ShouldBe("gone wrong");
        catalog.Modules.Get("flyback.honest.tone").ShouldNotBeNull();
    }

    [Fact]
    public void A_plugin_registering_a_module_under_another_name_than_declared_is_refused()
    {
        var catalog = PluginHost.LoadTypes(typeof(Misnamed));

        catalog.Plugins.ShouldBeEmpty();
        catalog.Problems.ShouldHaveSingleItem().Message.ShouldContain("declared as \"Tone\" and registered as \"Tune\"");
    }

    [Theory]
    [InlineData("1.0.0.0", false)]
    [InlineData("1.1.0.0", false)]
    [InlineData("1.2.0.0", true)]
    [InlineData("2.0.0.0", true)]
    public void Only_a_plugin_compiled_against_a_contract_with_declarations_is_held_to_them(string version, bool held)
    {
        AssemblyName[] references = [new(AssemblyFacts.Contract) { Version = Version.Parse(version) }];

        ModuleDeclarations.Required(references).ShouldBe(held);
    }

    /// <summary>What a shipped plugin declares is exactly what it registers, so the dialog lists no module that does not exist.</summary>
    [Theory]
    [InlineData("Picture", "flyback.picture")]
    [InlineData("Voice", "flyback.voice")]
    [InlineData("Effects", "flyback.effects")]
    [InlineData("Mastering", "flyback.mastering")]
    [InlineData("Sample", "flyback.sample")]
    [InlineData("Figures", "flyback.figures")]
    public void A_shipped_plugin_declares_exactly_the_modules_it_registers(string folder, string provider)
    {
        var declared = PluginDescription.OfFolder(Path.Combine(PluginHost.DefaultDirectory, folder))!.Modules;
        var registered = ShippedPlugins.Loaded.Modules.All.Where(m => m.TypeId.StartsWith(provider + ".", StringComparison.Ordinal));

        declared.Select(d => (d.TypeId, d.Name)).OrderBy(d => d.TypeId, StringComparer.Ordinal)
            .ShouldBe(registered.Select(m => (m.TypeId, m.Name)).OrderBy(d => d.TypeId, StringComparer.Ordinal));
    }
}

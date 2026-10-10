using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The question a plugin is asked before any of it runs: whether what it was
/// compiled against is something this host still has.
/// </summary>
public class ContractVersionTests
{
    private static readonly Version Offered = new(2, 3, 0, 0);

    private static AssemblyName BuiltAgainst(string version) => new($"Flyback.Core, Version={version}");

    [Theory]
    [InlineData("2.3.0.0")]
    [InlineData("2.0.0.0")]
    [InlineData("2.1.0.0")]
    public void A_plugin_built_against_this_contract_or_an_earlier_one_of_the_same_major_loads(string built) =>
        ContractVersion.Refusal(BuiltAgainst(built), Offered).ShouldBeNull();

    /// <summary>
    /// The case the check exists for. The runtime binds this reference to the
    /// host's copy without a word, and the member that went is missed later, on
    /// whichever thread first compiles the method that names it.
    /// </summary>
    [Theory]
    [InlineData("1.9.0.0")]
    [InlineData("0.3.0.0")]
    public void A_plugin_built_against_an_earlier_major_is_told_to_rebuild(string built)
    {
        var refusal = ContractVersion.Refusal(BuiltAgainst(built), Offered);

        refusal.ShouldNotBeNull();
        refusal.ShouldContain("needs rebuilding");
        refusal.ShouldContain("Flyback.Core " + built[..^2]);
        refusal.ShouldContain("2.3.0");
    }

    [Theory]
    [InlineData("2.4.0.0")]
    [InlineData("3.0.0.0")]
    public void A_plugin_built_against_a_later_contract_is_told_the_host_is_too_old(string built)
    {
        var refusal = ContractVersion.Refusal(BuiltAgainst(built), Offered);

        refusal.ShouldNotBeNull();
        refusal.ShouldContain("needs a newer Flyback");
    }

    [Fact]
    public void A_reference_that_records_no_version_is_not_held_against_a_plugin() =>
        ContractVersion.Refusal(new AssemblyName("Flyback.Core"), Offered).ShouldBeNull();

    /// <summary>
    /// What an assembly references that is not the contract is its own business,
    /// whatever version of it that is.
    /// </summary>
    [Fact]
    public void Only_the_two_contract_assemblies_are_asked_about() =>
        ContractVersion.Refusal(typeof(ShouldlyConfiguration).Assembly).ShouldBeNull();

    /// <summary>
    /// A host that offered a contract versioned with the release would refuse
    /// every plugin at every release, which is the thing this replaces.
    /// </summary>
    [Fact]
    public void The_two_contract_assemblies_carry_one_version_that_is_not_the_releases()
    {
        var core = typeof(NodeDef).Assembly.GetName().Version;
        var plugins = typeof(IFlybackPlugin).Assembly.GetName().Version;

        core.ShouldBe(plugins);
        core.ShouldNotBe(typeof(PatchWorkbenchTests).Assembly.GetName().Version);
    }

    [Fact]
    public void Everything_built_beside_this_host_is_offered_what_it_was_built_against() =>
        ContractVersion.Refusal(typeof(ContractVersionTests).Assembly).ShouldBeNull();
}

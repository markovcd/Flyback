using System.Reflection;
using Flyback.Core.Graph;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// What a plugin is compiled against, approved as text. A plugin somebody built
/// last month is not rebuilt when this repository changes, so the only warning
/// that one has been broken is this file changing.
/// </summary>
/// <remarks>
/// A failure here is a question rather than a fault, and the snapshot's first
/// line is where it is answered: a line gone or altered means every plugin built
/// before it may be naming something that is no longer there, which is a new
/// major of <c>PluginContractVersion</c> in <c>Directory.Build.props</c>; a
/// line added is a new minor. The version is part of the snapshot so that the
/// two cannot be approved apart.
/// </remarks>
public class ContractSurfaceTests
{
    public static TheoryData<string> Contract => ["Flyback.Core", "Flyback.Plugins"];

    private static Assembly Named(string name) =>
        name == "Flyback.Core" ? typeof(NodeDef).Assembly : typeof(IFlybackPlugin).Assembly;

    [Theory]
    [MemberData(nameof(Contract))]
    public async Task What_a_plugin_is_compiled_against_is_as_approved(string assembly)
    {
        var contract = Named(assembly);

        var text =
            $"{assembly} {contract.GetName().Version!.ToString(3)}\n\n" +
            ContractSurface.Of(contract);

        await Verify(text, "txt")
            .UseDirectory("snapshots")
            .UseFileName(assembly)
            .DisableRequireUniquePrefix();
    }
}

using System.CommandLine;
using System.Reflection;
using Flyback.Cli.Commands;
using Flyback.Host;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>Every command a <c>Commands</c> class builds is reachable from the root, so <c>--help</c> lists it.</summary>
public sealed class CommandRegistrationTests
{
    private static readonly PluginRegistry Registry = new(() => PluginCatalog.Empty, "nowhere", null);

    /// <summary>What a <c>Build</c> method may ask for; a new kind of parameter is added here.</summary>
    private static readonly Dictionary<Type, object> Arguments = new()
    {
        [typeof(PluginRegistry)] = Registry,
        [typeof(Option<bool>)] = new Option<bool>("--json"),
        [typeof(OutputSettings)] = new OutputSettings(),
    };

    public static TheoryData<string> Builders => [.. Built().Select(b => b.Type.Name)];

    [Theory]
    [MemberData(nameof(Builders))]
    public void Every_command_a_class_builds_is_reachable_from_the_root(string type)
    {
        var command = Built().Single(b => b.Type.Name == type).Command;

        Names(Program.Commands(Registry, new OutputSettings())).ShouldContain(
            command.Name,
            $"{type}.Build makes '{command.Name}', which Program.Commands never adds: it is missing from --help.");
    }

    [Fact]
    public void Every_command_class_is_found() => Built().Count().ShouldBeGreaterThan(10);

    /// <summary>Each class under <c>Flyback.Cli.Commands</c> with a static <c>Build</c> that returns a command, and what it builds.</summary>
    private static IEnumerable<(Type Type, Command Command)> Built() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(PluginCommand).Namespace)
            .SelectMany(t => t
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "Build" && m.ReturnType == typeof(Command))
                .Select(m => (Type: t, Command: (Command)m.Invoke(null, [.. m.GetParameters().Select(Argument)])!)));

    private static object Argument(ParameterInfo parameter) =>
        Arguments.TryGetValue(parameter.ParameterType, out var value)
            ? value
            : throw new InvalidOperationException(
                $"{parameter.Member.DeclaringType!.Name}.Build asks for a {parameter.ParameterType.Name}: add one to {nameof(Arguments)}.");

    /// <summary>The name of every command below <paramref name="command"/>, at any depth.</summary>
    private static IEnumerable<string> Names(Command command) =>
        command.Subcommands.SelectMany(c => (string[])[c.Name, .. Names(c)]);
}

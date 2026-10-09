using System.CommandLine;

namespace Flyback.Cli.Common;

/// <summary>The <c>--settings</c> a command that asks a decision model reads its choice of model from.</summary>
internal static class DecisionSettingsOption
{
    public static Option<string> Create() => new("--settings")
    {
        HelpName = "path",
        Description = "Read the decision model's settings from another settings.json than the editor's.",
    };
}

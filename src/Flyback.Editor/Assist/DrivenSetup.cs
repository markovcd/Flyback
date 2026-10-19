namespace Flyback.Editor.Assist;

/// <summary>What a person pastes to have Claude Code or Codex drive the editor through <c>flyback-cli mcp --editor</c>.</summary>
internal static class DrivenSetup
{
    /// <summary>The <c>flyback-cli</c> shipped beside the editor.</summary>
    public static string Cli => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "flyback-cli.exe" : "flyback-cli");

    /// <summary>The line that adds Flyback to Claude Code, then the lines that add it to Codex's <c>config.toml</c>.</summary>
    public static string Lines(string cli) =>
        $"""
        claude mcp add -s user flyback -- "{cli}" mcp --editor

        [mcp_servers.flyback]
        command = "{cli.Replace("\\", "\\\\")}"
        args = ["mcp", "--editor"]
        """;
}

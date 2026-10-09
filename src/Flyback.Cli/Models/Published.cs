namespace Flyback.Cli.Models;

/// <summary>What one run of the SDK came to: its exit code and everything it printed.</summary>
internal sealed record Published(int Code, string Output);

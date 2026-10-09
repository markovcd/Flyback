namespace Flyback.Cli.Models;

/// <summary>A preset the site is waiting on media for.</summary>
internal sealed record Waiting(string Id, string Name, string FileName);

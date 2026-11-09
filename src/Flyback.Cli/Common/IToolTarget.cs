using System.Text.Json;
using Flyback.Assist;
using Flyback.Cli.Models;

namespace Flyback.Cli.Common;

/// <summary>What <see cref="McpServer"/> runs its calls on: a patch file, or the open editor.</summary>
internal interface IToolTarget
{
    /// <summary>What the client is told it is editing, before the briefing.</summary>
    string Describes { get; }

    /// <summary>How an edit reaches the person, which decides what the tools say <c>propose</c> and <c>reset</c> do.</summary>
    Delivery Delivery { get; }

    /// <summary>The client's name, as it introduced itself.</summary>
    void Greet(string client);

    Task<ToolAnswered> Call(string tool, JsonElement arguments, CancellationToken cancel);

    /// <summary>Puts <paramref name="text"/> where the person reads it.</summary>
    Task<ToolAnswered> Say(string text, CancellationToken cancel);

    /// <summary>
    /// The briefing for the modules the target compiles with, or null where they are this
    /// program's own and its briefing serves.
    /// </summary>
    Task<string?> Briefing(CancellationToken cancel);
}

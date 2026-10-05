namespace Flyback.Plugins.Codex;

/// <summary>One entry of the conversation: who said it, what, and the pictures that went with it.</summary>
internal sealed record Turn(string Role, string Text, IReadOnlyList<byte[]> Pictures)
{
    /// <summary>The person's own words.</summary>
    public const string Person = "person";

    /// <summary>What the model said, calls included.</summary>
    public const string Model = "you";

    /// <summary>What the workbench answered the calls with.</summary>
    public const string Flyback = "flyback";

    public static bool IsRole(string? role) => role is Person or Model or Flyback;
}

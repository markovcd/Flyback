using Flyback.Plugins.Secrets;

namespace Flyback.Editor.Tests.Assist;

/// <summary>A secret store that keeps what it is given in memory, and reaches no operating system.</summary>
internal sealed class FakeSecretStore : ISecretStore
{
    public Dictionary<string, string> Held { get; } = new(StringComparer.Ordinal);

    public string Id => "fake";

    public string Name => "Fake store";

    public int Priority => 0;

    public bool IsSupported => true;

    public void Keep(string account, string secret) => Held[account] = secret;

    /// <summary>How many times <see cref="Recall"/> was asked.</summary>
    public int Recalls { get; private set; }

    public string? Recall(string account)
    {
        Recalls++;

        return Held.GetValueOrDefault(account);
    }

    public void Forget(string account) => Held.Remove(account);
}

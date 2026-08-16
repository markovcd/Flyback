using Flyback.App.Assist;
using Flyback.Plugins.Secrets;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Assist;

/// <summary>
/// Where a key comes from, and — as much as anything — where it does not go.
/// </summary>
public class CredentialsTests
{
    private const string Account = "anthropic";
    private const string Variable = "FLYBACK_TEST_KEY_THAT_IS_NOT_SET";

    [Fact]
    public void With_nothing_anywhere_there_is_no_key()
    {
        var credentials = new Credentials(null);

        credentials.Of(Account, Variable).ShouldBeNull();
        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.None);
    }

    [Fact]
    public void A_key_typed_in_with_nowhere_to_keep_it_lasts_the_session()
    {
        var credentials = new Credentials(null);

        credentials.CanKeep.ShouldBeFalse();
        credentials.Accept(Account, "sk-typed", keep: true);

        credentials.Of(Account, Variable).ShouldBe("sk-typed");
        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.Session);
    }

    [Fact]
    public void A_key_can_be_put_somewhere_that_outlives_the_window()
    {
        var store = new FakeStore();
        var credentials = new Credentials(store);

        credentials.CanKeep.ShouldBeTrue();
        credentials.Accept(Account, "sk-kept", keep: true);

        store.Held[Account].ShouldBe("sk-kept");
        new Credentials(store).SourceOf(Account, Variable).ShouldBe(CredentialSource.Kept);
    }

    [Fact]
    public void Declining_to_keep_a_key_really_does_not_keep_it()
    {
        var store = new FakeStore();
        var credentials = new Credentials(store);

        credentials.Accept(Account, "sk-passing-through", keep: false);

        store.Held.ShouldNotContainKey(Account);
        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.Session);
    }

    /// <summary>
    /// The environment wins, and nothing writes it back. Somebody who exports a
    /// key has said where it lives; Flyback taking a copy would be Flyback
    /// deciding otherwise.
    /// </summary>
    [Fact]
    public void The_environment_beats_everything_and_is_never_copied()
    {
        var store = new FakeStore();
        store.Held[Account] = "sk-kept";

        var credentials = new Credentials(store);
        var variable = "FLYBACK_TEST_KEY_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(variable, "sk-from-the-environment");

            credentials.Of(Account, variable).ShouldBe("sk-from-the-environment");
            credentials.SourceOf(Account, variable).ShouldBe(CredentialSource.Environment);

            credentials.Accept(Account, "sk-typed", keep: true);
            credentials.Of(Account, variable).ShouldBe("sk-from-the-environment");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>An exported-but-empty variable is the classic way to have set nothing.</summary>
    [Fact]
    public void An_empty_variable_counts_as_no_variable()
    {
        var credentials = new Credentials(null);
        var variable = "FLYBACK_TEST_KEY_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(variable, "   ");
            credentials.SourceOf(Account, variable).ShouldBe(CredentialSource.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Forgetting_a_key_removes_it_from_everywhere()
    {
        var store = new FakeStore();
        var credentials = new Credentials(store);

        credentials.Accept(Account, "sk-kept", keep: true);
        credentials.Forget(Account);

        store.Held.ShouldNotContainKey(Account);
        credentials.Of(Account, Variable).ShouldBeNull();
    }

    /// <summary>
    /// A store is a plugin, so it may be broken. A key that cannot be written
    /// still has to work for this run, and the window has to survive.
    /// </summary>
    [Fact]
    public void A_store_that_throws_costs_the_key_and_nothing_else()
    {
        var credentials = new Credentials(new ThrowingStore());

        Should.NotThrow(() => credentials.Accept(Account, "sk-typed", keep: true));

        credentials.Of(Account, Variable).ShouldBe("sk-typed");
        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.Session);

        Should.NotThrow(() => credentials.Forget(Account));
    }

    private sealed class FakeStore : ISecretStore
    {
        public Dictionary<string, string> Held { get; } = new(StringComparer.Ordinal);

        public string Id => "fake";

        public string Name => "Fake store";

        public int Priority => 0;

        public bool IsSupported => true;

        public void Keep(string account, string secret) => Held[account] = secret;

        public string? Recall(string account) => Held.GetValueOrDefault(account);

        public void Forget(string account) => Held.Remove(account);
    }

    private sealed class ThrowingStore : ISecretStore
    {
        public string Id => "broken";

        public string Name => "Broken store";

        public int Priority => 0;

        public bool IsSupported => true;

        public void Keep(string account, string secret) => throw new InvalidOperationException("no");

        public string? Recall(string account) => throw new InvalidOperationException("no");

        public void Forget(string account) => throw new InvalidOperationException("no");
    }
}

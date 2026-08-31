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

    /// <summary>
    /// A kept key says it was kept, in the same breath as being accepted.
    /// </summary>
    /// <remarks>
    /// <see cref="Credentials.Accept"/> holds a copy in memory whatever happens,
    /// so that a store which failed still leaves a working run — which meant a
    /// source read off the session alone called every key session-only, however
    /// safely it had just been written to disk. "Gone when this window closes"
    /// about a key that is not is the very lie ADR-0034 exists to prevent.
    /// </remarks>
    [Fact]
    public void A_key_that_was_kept_says_so_at_once_rather_than_next_launch()
    {
        var credentials = new Credentials(new FakeStore());

        credentials.Accept(Account, "sk-kept", keep: true);

        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.Kept);
        credentials.Of(Account, Variable).ShouldBe("sk-kept");
    }

    /// <summary>
    /// The other half of reading it back: a store that took the key and lost it
    /// must not be reported as having kept it. The run still works.
    /// </summary>
    [Fact]
    public void A_store_that_swallows_a_key_is_not_reported_as_keeping_it()
    {
        var credentials = new Credentials(new ForgetfulStore());

        credentials.Accept(Account, "sk-typed", keep: true);

        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.Session);
        credentials.Of(Account, Variable).ShouldBe("sk-typed");
    }

    /// <summary>
    /// Changing one's mind about keeping, without the secret being asked for a
    /// second time — the panel empties the field once a key has been taken, so
    /// there is nothing left to retype it from.
    /// </summary>
    [Fact]
    public void A_key_held_for_the_session_can_be_kept_afterwards()
    {
        var store = new FakeStore();
        var credentials = new Credentials(store);

        credentials.Accept(Account, "sk-typed", keep: false);
        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.Session);

        credentials.KeepWhatIsHeld(Account);

        store.Held[Account].ShouldBe("sk-typed");
        credentials.SourceOf(Account, Variable).ShouldBe(CredentialSource.Kept);
    }

    [Fact]
    public void There_is_nothing_to_keep_afterwards_when_nothing_was_entered()
    {
        var store = new FakeStore();

        Should.NotThrow(() => new Credentials(store).KeepWhatIsHeld(Account));

        store.Held.ShouldNotContainKey(Account);
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
    /// The environment answers when nothing was entered, and nothing writes it
    /// back. Somebody who exports a key has said where it lives; Flyback taking
    /// a copy would be Flyback deciding otherwise.
    /// </summary>
    [Fact]
    public void The_environment_answers_when_nothing_was_entered()
    {
        var credentials = new Credentials(new FakeStore());
        var variable = "FLYBACK_TEST_KEY_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(variable, "sk-from-the-environment");

            credentials.Of(Account, variable).ShouldBe("sk-from-the-environment");
            credentials.SourceOf(Account, variable).ShouldBe(CredentialSource.Environment);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>
    /// Why this order matters: without it, typing a key on a machine that
    /// exports one would do nothing at all — the field would simply empty
    /// itself and the panel would go on naming the variable.
    /// </summary>
    [Fact]
    public void A_key_entered_here_beats_the_environment()
    {
        var store = new FakeStore();
        var credentials = new Credentials(store);
        var variable = "FLYBACK_TEST_KEY_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(variable, "sk-from-the-environment");

            credentials.Accept(Account, "sk-typed", keep: true);

            credentials.Of(Account, variable).ShouldBe("sk-typed");

            // Kept rather than Session, because this one asked to be kept and
            // the store has it. Either way it is the entered key that answers,
            // which is the whole of what this is about.
            credentials.SourceOf(Account, variable).ShouldBe(CredentialSource.Kept);

            // And the environment is still exactly where it was. Preferring an
            // entered key is not the same as taking a copy of an exported one.
            store.Held[Account].ShouldBe("sk-typed");
            Environment.GetEnvironmentVariable(variable).ShouldBe("sk-from-the-environment");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>
    /// What makes preferring an entered key safe: there is a way back. Without
    /// this, one bad key typed in would be permanent on a machine whose variable
    /// was right all along.
    /// </summary>
    [Fact]
    public void Forgetting_an_entered_key_falls_back_to_the_environment()
    {
        var credentials = new Credentials(new FakeStore());
        var variable = "FLYBACK_TEST_KEY_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(variable, "sk-from-the-environment");

            credentials.Accept(Account, "sk-wrong", keep: true);
            credentials.Forget(Account);

            credentials.Of(Account, variable).ShouldBe("sk-from-the-environment");
            credentials.SourceOf(Account, variable).ShouldBe(CredentialSource.Environment);
            credentials.HasEntered(Account).ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>
    /// A key typed in and not kept is newer than whatever the store is holding,
    /// so it is the one that answers. The other way round is the same bug the
    /// environment order had, one layer down.
    /// </summary>
    [Fact]
    public void A_key_entered_without_keeping_it_beats_an_older_kept_one()
    {
        var store = new FakeStore();
        store.Held[Account] = "sk-kept-earlier";

        var credentials = new Credentials(store);
        credentials.Accept(Account, "sk-just-typed", keep: false);

        credentials.Of(Account, Variable).ShouldBe("sk-just-typed");
        store.Held[Account].ShouldBe("sk-kept-earlier");
    }

    [Fact]
    public void An_entered_key_is_known_about_even_when_it_is_the_one_in_force()
    {
        var credentials = new Credentials(null);

        credentials.HasEntered(Account).ShouldBeFalse();

        credentials.Accept(Account, "sk-typed", keep: false);

        credentials.HasEntered(Account).ShouldBeTrue();
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

    /// <summary>
    /// Takes a key without complaint and has nothing when asked for it back —
    /// the shape of a store that is present but not working, which is the case
    /// a write that did not throw says nothing about.
    /// </summary>
    private sealed class ForgetfulStore : ISecretStore
    {
        public string Id => "forgetful";

        public string Name => "Forgetful store";

        public int Priority => 0;

        public bool IsSupported => true;

        public void Keep(string account, string secret)
        {
        }

        public string? Recall(string account) => null;

        public void Forget(string account)
        {
        }
    }

    private sealed class ThrowingStore : ISecretStore
    {
        public string Id => "broken";

        public string Name => "Broken store";

        public int Priority => 0;

        public bool IsSupported => true;

        public void Keep(string account, string secret) => throw new InvalidOperationException("no");

        public string Recall(string account) => throw new InvalidOperationException("no");

        public void Forget(string account) => throw new InvalidOperationException("no");
    }
}

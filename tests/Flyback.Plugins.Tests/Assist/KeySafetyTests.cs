using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>Which keys are never sent, and where a key would cross the network readable.</summary>
public sealed class KeySafetyTests
{
    [Theory]
    [InlineData("sk-admin-abcdefghijklmnop")]
    [InlineData("  sk-admin-abcdefghijklmnop")]
    [InlineData("sk-ant-admin01-abcdefghijklmnop")]
    public void An_admin_key_is_refused(string key) =>
        KeySafety.Refused(key).ShouldNotBeNull().ShouldContain("admin key");

    [Theory]
    [InlineData("sk-proj-abcdefghijklmnop")]
    [InlineData("sk-ant-api03-abcdefghijklmnop")]
    [InlineData("AIzaSyabcdefghijklmnop")]
    [InlineData("x")]
    [InlineData(null)]
    public void An_ordinary_key_is_taken(string? key) => KeySafety.Refused(key).ShouldBeNull();

    [Theory]
    [InlineData("http://192.0.2.10:11434", true)]
    [InlineData("http://ollama.lan", true)]
    [InlineData("https://api.example.test", false)]
    [InlineData("http://localhost:11434", false)]
    [InlineData("http://127.0.0.1:8080", false)]
    [InlineData("http://[::1]:8080", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Only_plain_http_to_another_machine_is_readable_on_the_way(string? origin, bool readable) =>
        KeySafety.Cleartext(origin).ShouldBe(readable);
}

using Shouldly;
using Xunit;

namespace Flyback.Site.Tests;

public sealed class SiteAdminTests
{
    private static Func<string, string?> Environment(string? id, string? secret) => name => name switch
    {
        SiteAdmin.IdVariable => id,
        SiteAdmin.SecretVariable => secret,
        _ => null,
    };

    [Fact]
    public void The_token_rides_on_every_request()
    {
        using var client = SiteAdmin.Client("https://presets.example.org", Environment("id.access", "s3cret"), out var problem)!;

        problem.ShouldBeNull();
        client.BaseAddress.ShouldBe(new Uri("https://presets.example.org/"));
        client.DefaultRequestHeaders.GetValues("CF-Access-Client-Id").ShouldBe(["id.access"]);
        client.DefaultRequestHeaders.GetValues("CF-Access-Client-Secret").ShouldBe(["s3cret"]);
    }

    [Fact]
    public void Without_a_token_it_says_which_variables_and_never_their_values()
    {
        SiteAdmin.Client("https://presets.example.org", Environment("id.access", null), out var problem).ShouldBeNull();

        problem.ShouldBe("set FLYBACK_ACCESS_ID and FLYBACK_ACCESS_SECRET to the site's Access service token.");
        problem!.ShouldNotContain("id.access");
    }

    [Theory]
    [InlineData("presets.example.org")]
    [InlineData("ftp://presets.example.org")]
    public void A_site_that_is_not_a_whole_web_address_is_refused(string server)
    {
        SiteAdmin.Client(server, Environment("id", "secret"), out var problem).ShouldBeNull();
        problem!.ShouldContain("whole address");
    }
}

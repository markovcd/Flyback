using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.LinuxIO.Tests;

/// <summary>What the JACK output offers, with or without a server, on a stand-in for one.</summary>
public class JackAudioOutputTests
{
    [Fact]
    public void Where_no_server_runs_it_asks_nothing()
    {
        var jack = new JackAudioOutput(running: () => false);

        jack.IsSupported.ShouldBeFalse();
        jack.Form(SettingValues.None).ShouldBeEmpty();
    }

    [Fact]
    public void Where_a_server_runs_it_asks_where_the_ports_connect()
    {
        var jack = new JackAudioOutput(running: () => true);

        jack.IsSupported.ShouldBeTrue();
        jack.Form(SettingValues.None).ShouldHaveSingleItem().Key.ShouldBe(JackAudioOutput.ConnectKey);
    }
}

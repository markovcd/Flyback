using System.Buffers.Binary;
using System.Net;
using Flyback.Host.Ableton;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Ableton;

/// <summary>Ableton Link's messages are written byte for byte as the protocol lays them out, and read back without trusting the sender.</summary>
public class AbletonWireTests
{
    private static readonly AbletonTimeline Tempo = new(468_750, 12_500_000, 9_876_543);

    private static readonly AbletonStartStop Playing = new(true, 8_000_000, 5_000_000);

    private static readonly IPEndPoint Answers = new(IPAddress.Parse("192.168.0.4"), 51234);

    [Fact]
    public void An_announcement_is_laid_out_as_the_protocol_has_it()
    {
        var bytes = AbletonWire.Announce(AbletonWire.Alive, 0x0102030405060708, 0x1112131415161718, Tempo, Playing, Answers);

        bytes.AsSpan(0, 8).ToArray().ShouldBe("_asdp_v\u0001"u8.ToArray());
        bytes[8].ShouldBe(AbletonWire.Alive);
        bytes[9].ShouldBe(AbletonWire.Ttl);
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10)).ShouldBe((ushort)0);
        BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(12)).ShouldBe(0x0102030405060708UL);

        // tmln: tempo, beat origin and time origin.
        Entry(bytes, 20).ShouldBe(("tmln", 24));
        BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(28)).ShouldBe(468_750);
        BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(36)).ShouldBe(12_500_000);
        BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(44)).ShouldBe(9_876_543);

        Entry(bytes, 52).ShouldBe(("sess", 8));
        BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(60)).ShouldBe(0x1112131415161718UL);

        Entry(bytes, 68).ShouldBe(("stst", 17));
        bytes[76].ShouldBe((byte)1);

        Entry(bytes, 93).ShouldBe(("mep4", 6));
        bytes.AsSpan(101, 4).ToArray().ShouldBe(new byte[] { 192, 168, 0, 4 });
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(105)).ShouldBe((ushort)51234);

        bytes.Length.ShouldBe(107);
    }

    [Fact]
    public void An_announcement_reads_back_as_it_was_written()
    {
        var bytes = AbletonWire.Announce(AbletonWire.Response, 7, 9, Tempo, Playing, Answers);

        AbletonWire.TryRead(bytes, out var message).ShouldBeTrue();

        message.ShouldBe(new AbletonMessage(AbletonWire.Response, AbletonWire.Ttl, 7, 9, Tempo, Playing, Answers));
    }

    [Fact]
    public void An_entry_a_newer_app_sends_is_passed_by()
    {
        byte[] extra = [.. "aep4"u8, 0, 0, 0, 6, 10, 0, 0, 1, 0, 80];
        byte[] bytes = [.. AbletonWire.Announce(AbletonWire.Alive, 7, 9, Tempo, Playing, Answers), .. extra];

        AbletonWire.TryRead(bytes, out var message).ShouldBeTrue();
        message.Timeline.ShouldBe(Tempo);
    }

    [Fact]
    public void An_entry_longer_than_the_packet_rejects_it()
    {
        var bytes = AbletonWire.Announce(AbletonWire.Alive, 7, 9, Tempo, Playing, Answers);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24), 4_000_000_000);

        AbletonWire.TryRead(bytes, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_tempo_of_no_microseconds_a_beat_rejects_it()
    {
        var bytes = AbletonWire.Announce(AbletonWire.Alive, 7, 9, Tempo with { MicrosPerBeat = 0 }, Playing, Answers);

        AbletonWire.TryRead(bytes, out _).ShouldBeFalse();
    }

    [Fact]
    public void Another_group_and_another_protocol_are_not_read()
    {
        var bytes = AbletonWire.Announce(AbletonWire.Alive, 7, 9, Tempo, Playing, Answers);
        var grouped = bytes.ToArray();
        grouped[11] = 1;

        AbletonWire.TryRead(grouped, out _).ShouldBeFalse();
        AbletonWire.TryRead(AbletonWire.PingAt(5, 0), out _).ShouldBeFalse();
    }

    [Fact]
    public void A_goodbye_names_only_the_app()
    {
        AbletonWire.TryRead(AbletonWire.Goodbye(7), out var message).ShouldBeTrue();

        message.Kind.ShouldBe(AbletonWire.ByeBye);
        message.Node.ShouldBe(7UL);
        message.Timeline.ShouldBeNull();
    }

    [Fact]
    public void A_pong_carries_the_session_the_ghost_time_and_the_ping_back()
    {
        var ping = AbletonWire.PingAt(1_000, 2_000);

        AbletonWire.TryReadPing(ping, out var payload).ShouldBeTrue();
        var pong = AbletonWire.PongTo(9, 3_000, payload);

        AbletonWire.TryReadPong(pong, out var read).ShouldBeTrue();
        read.ShouldBe(new AbletonPong(9, 3_000, 2_000, 1_000));
    }

    [Fact]
    public void A_ping_longer_than_a_ping_is_not_answered()
    {
        byte[] padded = [.. AbletonWire.PingAt(1_000, 2_000), .. "junk"u8, 0, 0, 0, 0];

        AbletonWire.TryReadPing(padded, out _).ShouldBeFalse();
    }

    private static (string Key, int Size) Entry(byte[] bytes, int at) =>
        (System.Text.Encoding.ASCII.GetString(bytes, at, 4), (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 4)));
}

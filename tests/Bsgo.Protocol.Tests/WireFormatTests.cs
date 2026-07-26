using System.Buffers;
using System.Numerics;
using System.Text;
using Bsgo.Protocol;
using Xunit;

namespace Bsgo.Protocol.Tests;

/// <summary>
/// Verifies the wire format byte by byte. The expected bytes are written by
/// hand from <c>spec/wire-format.md</c>: if a test fails, either the encoder is
/// wrong or the spec is.
/// </summary>
public class WireFormatTests
{
    // --- lengths ---------------------------------------------------------

    [Fact]
    public void String_uses_a_u16_byte_prefix_not_leb128()
    {
        var w = new BgoWriter();
        w.Write("hola");

        // 04 00 = u16 length; with LEB128 it would be the single byte 0x04.
        Assert.Equal(new byte[] { 0x04, 0x00, (byte)'h', (byte)'o', (byte)'l', (byte)'a' }, w.ToArray());
    }

    [Fact]
    public void String_counts_utf8_bytes_not_characters()
    {
        var w = new BgoWriter();
        w.Write("ñá");   // 2 characters, 4 bytes in UTF-8

        var bytes = w.ToArray();
        Assert.Equal(4, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(6, bytes.Length);
    }

    [Fact]
    public void Empty_string_is_just_a_zero_length()
    {
        var w = new BgoWriter();
        w.Write("");
        Assert.Equal(new byte[] { 0x00, 0x00 }, w.ToArray());
    }

    // --- scalars ---------------------------------------------------------

    [Fact]
    public void Writer_accepts_zero_capacity_for_messages_without_payload()
    {
        // Regression: the handshake greeting has no payload and used to blow
        // up when building the writer with capacity 0.
        var w = new BgoWriter(0);
        Assert.Equal(0, w.Length);

        w.Write((byte)1);   // and it still grows normally
        Assert.Equal(1, w.Length);
    }

    [Fact]
    public void Integers_are_little_endian()
    {
        var w = new BgoWriter();
        w.Write((uint)0x11223344);
        Assert.Equal(new byte[] { 0x44, 0x33, 0x22, 0x11 }, w.ToArray());
    }

    [Fact]
    public void Colour_is_one_byte_per_channel_scaled_to_255()
    {
        var w = new BgoWriter();
        w.Write(BgoColor.FromUnit(1f, 0f, 0.5f, 1f));

        var bytes = w.ToArray();
        Assert.Equal(4, bytes.Length);
        Assert.Equal(255, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(127, bytes[2]);   // truncated, not rounded
        Assert.Equal(255, bytes[3]);
    }

    // --- round-trip ------------------------------------------------------

    [Fact]
    public void Roundtrip_of_every_wire_type()
    {
        var w = new BgoWriter();
        w.Write(true);
        w.Write((byte)0xAB);
        w.Write((short)-1234);
        w.Write((ushort)54321);
        w.Write(-123456789);
        w.Write(3141592653u);
        w.Write(1.5f);
        w.Write("Battlestar Galáctica");
        w.Write(new Vector3(1f, -2f, 3.5f));
        w.Write(new Euler3(10f, 20f, 30f));
        w.Write(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
        w.Write(BgoColor.FromUnit(1f, 1f, 1f, 1f));
        w.Write(new Tick(987654));
        w.Write(new[] { "alfa", "beta" });

        var r = new BgoReader(w.WrittenSpan);
        Assert.True(r.ReadBool());
        Assert.Equal(0xAB, r.ReadByte());
        Assert.Equal(-1234, r.ReadInt16());
        Assert.Equal(54321, r.ReadUInt16());
        Assert.Equal(-123456789, r.ReadInt32());
        Assert.Equal(3141592653u, r.ReadUInt32());
        Assert.Equal(1.5f, r.ReadSingle());
        Assert.Equal("Battlestar Galáctica", r.ReadString());
        Assert.Equal(new Vector3(1f, -2f, 3.5f), r.ReadVector3());
        Assert.Equal(new Euler3(10f, 20f, 30f), r.ReadEuler3());
        Assert.Equal(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), r.ReadQuaternion());
        Assert.Equal(new BgoColor(255, 255, 255, 255), r.ReadColor());
        Assert.Equal(new Tick(987654), r.ReadTick());
        Assert.Equal(new[] { "alfa", "beta" }, r.ReadStringArray());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Reading_past_the_buffer_throws_a_protocol_exception()
    {
        Assert.Throws<BgoProtocolException>(() =>
        {
            var inner = new BgoReader(new byte[] { 0x01 });
            inner.ReadByte();
            inner.ReadInt32();
        });
    }
}

/// <summary>Verifies message framing over the TCP stream.</summary>
public class FramingTests
{
    [Fact]
    public void Frame_writes_length_protocol_and_type_in_order()
    {
        var payload = new byte[] { 0xDE, 0xAD };
        var frame = BgoFraming.Frame(ProtocolId.Login, 7, payload);

        // body = 1 (protocol) + 2 (type) + 2 (payload) = 5
        // Length is BIG-endian (00 05); the message type little (07 00).
        Assert.Equal(new byte[] { 0x00, 0x05, (byte)ProtocolId.Login, 0x07, 0x00, 0xDE, 0xAD }, frame);
    }

    [Fact]
    public void The_length_prefix_is_big_endian()
    {
        // Regression: writing it little-endian left the client waiting forever
        // for a message of nonsensical size.
        // Body of 0x0102 = 258 bytes -> 255 payload + 3 header.
        var frame = BgoFraming.Frame(ProtocolId.Game, 1, new byte[255]);

        Assert.Equal(0x01, frame[0]);   // high byte first
        Assert.Equal(0x02, frame[1]);
    }

    [Fact]
    public void The_message_type_stays_little_endian()
    {
        var frame = BgoFraming.Frame(ProtocolId.Game, 0x0102, []);

        Assert.Equal(0x02, frame[3]);   // low byte first
        Assert.Equal(0x01, frame[4]);
    }

    [Fact]
    public void TryReadFrame_recovers_header_and_payload()
    {
        var frame = BgoFraming.Frame(ProtocolId.Game, 42, new byte[] { 1, 2, 3 });
        var input = new ReadOnlySequence<byte>(frame);

        Assert.True(BgoFraming.TryReadFrame(ref input, out var header, out var payload));
        Assert.Equal(ProtocolId.Game, header.Protocol);
        Assert.Equal(42, header.MessageType);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.ToArray());
        Assert.Equal(0, input.Length);   // fully consumed
    }

    [Fact]
    public void TryReadFrame_consumes_nothing_when_the_message_is_incomplete()
    {
        var frame = BgoFraming.Frame(ProtocolId.Game, 42, new byte[] { 1, 2, 3 });
        var partial = new ReadOnlySequence<byte>(frame[..^1]);   // 1 byte short
        var before = partial.Length;

        Assert.False(BgoFraming.TryReadFrame(ref partial, out _, out _));
        Assert.Equal(before, partial.Length);
    }

    [Fact]
    public void TryReadFrame_decodes_consecutive_messages()
    {
        var first = BgoFraming.Frame(ProtocolId.Login, 1, new byte[] { 0xAA });
        var second = BgoFraming.Frame(ProtocolId.Zone, 2, new byte[] { 0xBB, 0xCC });
        var input = new ReadOnlySequence<byte>([.. first, .. second]);

        Assert.True(BgoFraming.TryReadFrame(ref input, out var h1, out var p1));
        Assert.Equal(ProtocolId.Login, h1.Protocol);
        Assert.Equal(new byte[] { 0xAA }, p1.ToArray());

        Assert.True(BgoFraming.TryReadFrame(ref input, out var h2, out var p2));
        Assert.Equal(ProtocolId.Zone, h2.Protocol);
        Assert.Equal(new byte[] { 0xBB, 0xCC }, p2.ToArray());

        Assert.Equal(0, input.Length);
    }

    [Fact]
    public void TryReadFrame_rejects_a_body_that_is_too_short()
    {
        // declared body of 2 bytes (big-endian): too small for protocol (1) + type (2)
        var malformed = new ReadOnlySequence<byte>(new byte[] { 0x00, 0x02, 0x00, 0x00 });
        Assert.Throws<BgoProtocolException>(() =>
        {
            var input = malformed;
            BgoFraming.TryReadFrame(ref input, out _, out _);
        });
    }
}

/// <summary>Checks the generated enums match the specification.</summary>
public class GeneratedProtocolTests
{
    [Fact]
    public void ProtocolId_covers_all_25_protocols()
    {
        Assert.Equal(25, Enum.GetValues<ProtocolId>().Length);
        Assert.Equal(0, (byte)ProtocolId.Login);   // first in the enum
    }

    [Fact]
    public void Login_declares_the_known_messages()
    {
        Assert.Equal(1, (ushort)LoginRequest.Init);
        Assert.Equal(2, (ushort)LoginRequest.Player);
        Assert.Equal(5, (ushort)LoginRequest.Echo);
        Assert.Equal(0, (ushort)LoginReply.Hello);
    }
}

using System.Buffers;
using System.Buffers.Binary;

namespace Bsgo.Protocol;

/// <summary>Header of a decoded message.</summary>
/// <param name="Protocol">Target protocol (selects the handler).</param>
/// <param name="MessageType">Member of that protocol's <c>Request</c>/<c>Reply</c> enum.</param>
public readonly record struct BgoMessageHeader(ProtocolId Protocol, ushort MessageType);

/// <summary>
/// Message framing: <c>[u16 length][u8 protocol][u16 type][payload]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The length covers the body (protocol + type + payload), not its own 2 bytes.
/// </para>
/// <para>
/// <b>The length prefix is big-endian</b>, unlike everything else in this
/// protocol. It is the only exception and nothing signals it: written
/// little-endian, the client reads a nonsensical size and waits forever for
/// bytes that never arrive. See <c>spec/wire-format.md</c>.
/// </para>
/// </remarks>
public static class BgoFraming
{
    /// <summary>Header bytes inside the body: <c>u8</c> protocol + <c>u16</c> type.</summary>
    public const int BodyHeaderSize = 3;

    /// <summary>Bytes of the length prefix that precedes the body.</summary>
    public const int LengthPrefixSize = 2;

    /// <summary>Largest body addressable by a <c>u16</c> prefix.</summary>
    public const int MaxBodySize = ushort.MaxValue;

    /// <summary>
    /// Tries to pull one complete message off the front of <paramref name="input"/>.
    /// </summary>
    /// <returns>
    /// <c>false</c> if not enough bytes have arrived yet; in that case nothing
    /// is consumed and the caller should wait for more data.
    /// </returns>
    public static bool TryReadFrame(
        ref ReadOnlySequence<byte> input,
        out BgoMessageHeader header,
        out ReadOnlySequence<byte> payload)
    {
        header = default;
        payload = default;

        if (input.Length < LengthPrefixSize) return false;

        Span<byte> lengthBytes = stackalloc byte[LengthPrefixSize];
        input.Slice(0, LengthPrefixSize).CopyTo(lengthBytes);
        int bodyLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);   // big-endian: see remarks

        if (bodyLength < BodyHeaderSize)
            throw new BgoProtocolException(
                $"body of {bodyLength} bytes: too small to hold the protocol and message type");

        long frameLength = LengthPrefixSize + bodyLength;
        if (input.Length < frameLength) return false;   // incomplete message

        var body = input.Slice(LengthPrefixSize, bodyLength);

        Span<byte> bodyHeader = stackalloc byte[BodyHeaderSize];
        body.Slice(0, BodyHeaderSize).CopyTo(bodyHeader);
        header = new BgoMessageHeader(
            (ProtocolId)bodyHeader[0],
            BinaryPrimitives.ReadUInt16LittleEndian(bodyHeader[1..]));

        payload = body.Slice(BodyHeaderSize);
        input = input.Slice(frameLength);
        return true;
    }

    /// <summary>
    /// Wraps an already serialised payload into a complete message, ready to send.
    /// </summary>
    public static byte[] Frame(ProtocolId protocol, ushort messageType, ReadOnlySpan<byte> payload)
    {
        int bodyLength = BodyHeaderSize + payload.Length;
        if (bodyLength > MaxBodySize)
            throw new BgoProtocolException(
                $"body of {bodyLength} bytes: exceeds the u16 prefix maximum of {MaxBodySize}");

        var frame = new byte[LengthPrefixSize + bodyLength];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)bodyLength);   // big-endian: see remarks
        frame[LengthPrefixSize] = (byte)protocol;
        // The message type is little-endian, like the rest of the body.
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(LengthPrefixSize + 1), messageType);
        payload.CopyTo(frame.AsSpan(LengthPrefixSize + BodyHeaderSize));
        return frame;
    }

    /// <summary>Shorthand for framing whatever a <see cref="BgoWriter"/> holds.</summary>
    public static byte[] Frame(ProtocolId protocol, ushort messageType, BgoWriter payload) =>
        Frame(protocol, messageType, payload.WrittenSpan);
}

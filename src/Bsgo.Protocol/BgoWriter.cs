using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Bsgo.Protocol;

/// <summary>
/// Writer for the BSGO wire format. Exact counterpart of
/// <see cref="BgoReader"/>: every length prefix is a <c>u16</c>.
/// </summary>
public sealed class BgoWriter
{
    private readonly ArrayBufferWriter<byte> _buffer;

    /// <param name="initialCapacity">
    /// Initial capacity in bytes. Zero is allowed (messages with no payload,
    /// such as the handshake greeting); <see cref="ArrayBufferWriter{T}"/>
    /// requires a positive value, so it is clamped rather than jumping to the
    /// default 256 the empty case does not need.
    /// </param>
    public BgoWriter(int initialCapacity = 256) =>
        _buffer = new ArrayBufferWriter<byte>(Math.Max(1, initialCapacity));

    public int Length => _buffer.WrittenCount;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;
    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
    public void Clear() => _buffer.Clear();

    private Span<byte> Advance(int count)
    {
        var span = _buffer.GetSpan(count)[..count];
        _buffer.Advance(count);
        return span;
    }

    // --- scalars ---------------------------------------------------------

    public void Write(bool value) => Write((byte)(value ? 1 : 0));
    public void Write(byte value) => Advance(1)[0] = value;
    public void Write(sbyte value) => Write((byte)value);

    public void Write(short value) => BinaryPrimitives.WriteInt16LittleEndian(Advance(2), value);
    public void Write(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Advance(2), value);
    public void Write(int value) => BinaryPrimitives.WriteInt32LittleEndian(Advance(4), value);
    public void Write(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Advance(4), value);
    public void Write(long value) => BinaryPrimitives.WriteInt64LittleEndian(Advance(8), value);
    public void Write(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Advance(8), value);
    public void Write(float value) => BinaryPrimitives.WriteSingleLittleEndian(Advance(4), value);
    public void Write(double value) => BinaryPrimitives.WriteDoubleLittleEndian(Advance(8), value);

    public void Write(Tick value) => Write(value.Value);

    /// <summary>Length prefix: always a <c>u16</c>.</summary>
    public void WriteLength(int count)
    {
        if (count is < 0 or > ushort.MaxValue)
            throw new BgoProtocolException($"length {count} is outside the u16 range");
        Write((ushort)count);
    }

    // --- composites ------------------------------------------------------

    /// <summary><c>u16</c> byte length + UTF-8. Note: the length counts BYTES, not characters.</summary>
    public void Write(string value)
    {
        if (string.IsNullOrEmpty(value)) { WriteLength(0); return; }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteLength(byteCount);
        Encoding.UTF8.GetBytes(value, Advance(byteCount));
    }

    public void Write(ReadOnlySpan<byte> value)
    {
        WriteLength(value.Length);
        value.CopyTo(Advance(value.Length));
    }

    /// <summary>
    /// Copies bytes verbatim, <b>without</b> a length prefix. For forwarding
    /// opaque payloads that already carry their own structure.
    /// </summary>
    public void WriteRaw(ReadOnlySpan<byte> value) => value.CopyTo(Advance(value.Length));

    public void Write(string[] values)
    {
        WriteLength(values.Length);
        foreach (var v in values) Write(v);
    }

    public void Write(Vector2 v) { Write(v.X); Write(v.Y); }
    public void Write(Vector3 v) { Write(v.X); Write(v.Y); Write(v.Z); }
    public void Write(Euler3 e) { Write(e.Pitch); Write(e.Yaw); Write(e.Roll); }
    public void Write(Quaternion q) { Write(q.X); Write(q.Y); Write(q.Z); Write(q.W); }
    public void Write(BgoColor c) { Write(c.R); Write(c.G); Write(c.B); Write(c.A); }

    /// <summary>Writes a descriptor: no header, the field order is the contract.</summary>
    public void Write(IBgoWritable desc) => desc.Write(this);

    public void WriteDescList<T>(IReadOnlyList<T> items) where T : IBgoWritable
    {
        WriteLength(items.Count);
        foreach (var item in items) item.Write(this);
    }

    public void WriteList(IReadOnlyList<ushort> items)
    {
        WriteLength(items.Count);
        foreach (var item in items) Write(item);
    }

    public void WriteList(IReadOnlyList<uint> items)
    {
        WriteLength(items.Count);
        foreach (var item in items) Write(item);
    }
}

/// <summary>A descriptor that can be written to the wire.</summary>
public interface IBgoWritable
{
    void Write(BgoWriter w);
}

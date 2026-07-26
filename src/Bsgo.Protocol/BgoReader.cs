using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Bsgo.Protocol;

/// <summary>
/// Reader for the BSGO wire format, over an in-memory buffer.
/// </summary>
/// <remarks>
/// A <c>ref struct</c> with a cursor: no allocations and no streams. The
/// encoding rules live in <c>spec/wire-format.md</c>; the critical difference
/// from <see cref="BinaryReader"/> is that every length is a <c>u16</c>, not a
/// 7-bit compressed integer.
/// </remarks>
public ref struct BgoReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buffer = buffer;
    private int _pos = 0;

    public readonly int Position => _pos;
    public readonly int Remaining => _buffer.Length - _pos;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
            throw new BgoProtocolException(
                $"read out of range: asked for {count} bytes, {Remaining} left");
        var slice = _buffer.Slice(_pos, count);
        _pos += count;
        return slice;
    }

    // --- scalars ---------------------------------------------------------

    public bool ReadBool() => ReadByte() != 0;
    public byte ReadByte() => Take(1)[0];
    public sbyte ReadSByte() => (sbyte)ReadByte();

    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    /// <summary>Length prefix: always a <c>u16</c>.</summary>
    public int ReadLength() => ReadUInt16();

    /// <summary>Game object identifier.</summary>
    public uint ReadGuid() => ReadUInt32();

    public Tick ReadTick() => new(ReadInt32());

    // --- composites ------------------------------------------------------

    /// <summary><c>u16</c> byte length + UTF-8.</summary>
    public string ReadString()
    {
        int byteCount = ReadLength();
        return byteCount == 0 ? string.Empty : Encoding.UTF8.GetString(Take(byteCount));
    }

    public byte[] ReadByteArray() => Take(ReadLength()).ToArray();

    public string[] ReadStringArray()
    {
        var result = new string[ReadLength()];
        for (int i = 0; i < result.Length; i++) result[i] = ReadString();
        return result;
    }

    public Vector2 ReadVector2() => new(ReadSingle(), ReadSingle());
    public Vector3 ReadVector3() => new(ReadSingle(), ReadSingle(), ReadSingle());
    public Euler3 ReadEuler3() => new(ReadSingle(), ReadSingle(), ReadSingle());
    public Quaternion ReadQuaternion() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
    public BgoColor ReadColor() => new(ReadByte(), ReadByte(), ReadByte(), ReadByte());

    /// <summary>Reads a descriptor: no header, the field order is the contract.</summary>
    public T ReadDesc<T>() where T : IBgoReadable, new()
    {
        var value = new T();
        value.Read(ref this);
        return value;
    }

    public List<T> ReadDescList<T>() where T : IBgoReadable, new()
    {
        int count = ReadLength();
        var list = new List<T>(count);
        for (int i = 0; i < count; i++) list.Add(ReadDesc<T>());
        return list;
    }

    public List<ushort> ReadUInt16List()
    {
        int count = ReadLength();
        var list = new List<ushort>(count);
        for (int i = 0; i < count; i++) list.Add(ReadUInt16());
        return list;
    }

    public List<uint> ReadUInt32List()
    {
        int count = ReadLength();
        var list = new List<uint>(count);
        for (int i = 0; i < count; i++) list.Add(ReadUInt32());
        return list;
    }

    /// <summary>
    /// Reads a compressed block (<c>u16</c> length + zlib data) and returns its
    /// decompressed contents, ready to wrap in another <see cref="BgoReader"/>.
    /// </summary>
    public byte[] ReadZipped()
    {
        var compressed = Take(ReadLength());
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}

/// <summary>A descriptor that can be read from the wire.</summary>
public interface IBgoReadable
{
    void Read(ref BgoReader r);
}

public sealed class BgoProtocolException(string message) : Exception(message);

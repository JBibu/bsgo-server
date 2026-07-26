using Bsgo.Protocol;

namespace Bsgo.Server.Players;

/// <summary>
/// The appearance chosen for a character: which piece fills each slot.
/// </summary>
/// <remarks>
/// On the wire it is a list of (slot, piece name) pairs followed by an extra
/// block with its own length. The client reads that block and discards it, but
/// it <b>must be there</b>: without it the reader stops halfway and shifts
/// everything that follows.
/// </remarks>
public sealed class AvatarDescription : IBgoWritable
{
    public Dictionary<AvatarItem, string> Items { get; init; } = [];

    public bool IsEmpty => Items.Count == 0;

    public string? this[AvatarItem item] => Items.GetValueOrDefault(item);

    public static AvatarDescription Read(ref BgoReader r)
    {
        int count = r.ReadLength();
        // Each entry needs at least 3 bytes, so a short message claiming 65535
        // of them cannot make us reserve megabytes up front.
        var items = new Dictionary<AvatarItem, string>(Math.Min(count, r.Remaining / 3));
        for (int i = 0; i < count; i++)
        {
            var key = (AvatarItem)r.ReadByte();
            items[key] = r.ReadString();
        }

        // Extra block, opaque to the client and to us.
        r.ReadByteArray();

        return new AvatarDescription { Items = items };
    }

    public void Write(BgoWriter w)
    {
        w.WriteLength(Items.Count);
        foreach (var (key, value) in Items)
        {
            w.Write((byte)key);
            w.Write(value);
        }

        w.WriteLength(0);   // empty extra block
    }

    /// <summary>Serialises to bytes, for storing verbatim.</summary>
    public byte[] ToBytes()
    {
        var w = new BgoWriter();
        Write(w);
        return w.ToArray();
    }

    /// <summary>Rebuilds from the stored bytes.</summary>
    public static AvatarDescription FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return new AvatarDescription();
        var r = new BgoReader(bytes);
        return Read(ref r);
    }
}

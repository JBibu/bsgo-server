using Bsgo.Protocol;
using Bsgo.Server.Players;
using Xunit;

namespace Bsgo.Server.Tests;

/// <summary>
/// The character's appearance travels as (slot, piece) pairs plus an extra
/// block. That block is easy to forget and its absence fails invisibly: it
/// shifts the reading of everything that follows.
/// </summary>
public class AvatarDescriptionTests
{
    private static AvatarDescription SampleCylon() => new()
    {
        Items = new Dictionary<AvatarItem, string>
        {
            [AvatarItem.Race] = "cylon",
            [AvatarItem.Sex] = "centurion",
            [AvatarItem.CylonHead] = "centurion_head_v1",
            [AvatarItem.CylonHeadSkin] = "",
        },
    };

    [Fact]
    public void Roundtrip_preserves_every_slot()
    {
        var original = SampleCylon();
        var restored = AvatarDescription.FromBytes(original.ToBytes());

        Assert.Equal(original.Items, restored.Items);
        Assert.Equal("centurion_head_v1", restored[AvatarItem.CylonHead]);
        Assert.Equal("", restored[AvatarItem.CylonHeadSkin]);
    }

    [Fact]
    public void The_extra_block_is_written_at_the_end()
    {
        var w = new BgoWriter();
        SampleCylon().Write(w);

        var r = new BgoReader(w.WrittenSpan);
        int count = r.ReadLength();
        Assert.Equal(4, count);
        for (int i = 0; i < count; i++)
        {
            r.ReadByte();
            r.ReadString();
        }

        // The extra block: present and empty.
        Assert.Equal(0, r.ReadLength());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void The_slot_travels_as_a_single_byte()
    {
        var avatar = new AvatarDescription
        {
            Items = new Dictionary<AvatarItem, string> { [AvatarItem.CylonLegs] = "centurion_legs_v2" },
        };

        var w = new BgoWriter();
        avatar.Write(w);

        var bytes = w.ToArray();
        Assert.Equal(1, bytes[0]);                          // one slot (u16 LE)
        Assert.Equal(0, bytes[1]);
        Assert.Equal((byte)AvatarItem.CylonLegs, bytes[2]); // 18
    }

    [Fact]
    public void An_empty_avatar_is_recognised_as_such()
    {
        Assert.True(new AvatarDescription().IsEmpty);
        Assert.True(AvatarDescription.FromBytes([]).IsEmpty);
        Assert.False(SampleCylon().IsEmpty);
    }

    [Fact]
    public void An_unassigned_slot_returns_null_instead_of_throwing()
    {
        Assert.Null(SampleCylon()[AvatarItem.HumanHair]);
    }
}

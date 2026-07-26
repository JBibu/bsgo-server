using System.Numerics;

namespace Bsgo.Protocol;

/// <summary>
/// A rotation as it travels on the wire: pitch/yaw/roll in degrees.
/// </summary>
/// <remarks>
/// Not a <see cref="Vector3"/> in disguise. The client interprets it with
/// Unity's rotation order, so converting to a quaternion as if it were x/y/z
/// misaligns ship orientation.
/// </remarks>
public readonly record struct Euler3(float Pitch, float Yaw, float Roll)
{
    /// <summary>Converts to a quaternion using Unity's order (Z, X, Y).</summary>
    public Quaternion ToQuaternion()
    {
        const float deg2Rad = MathF.PI / 180f;
        // Unity applies the rotations in the order Z (roll), X (pitch), Y (yaw).
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw * deg2Rad)
             * Quaternion.CreateFromAxisAngle(Vector3.UnitX, Pitch * deg2Rad)
             * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Roll * deg2Rad);
    }
}

/// <summary>An RGBA colour, one byte per channel on the wire.</summary>
public readonly record struct BgoColor(byte R, byte G, byte B, byte A)
{
    /// <summary>Builds one from normalised 0..1 channels, as Unity uses them.</summary>
    public static BgoColor FromUnit(float r, float g, float b, float a) =>
        new((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), (byte)(a * 255f));
}

/// <summary>A server timestamp. An <c>i32</c> on the wire.</summary>
public readonly record struct Tick(int Value)
{
    public static implicit operator int(Tick t) => t.Value;
    public static implicit operator Tick(int v) => new(v);
}

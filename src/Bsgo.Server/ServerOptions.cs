namespace Bsgo.Server;

/// <summary>Server configuration (the <c>Server</c> section of appsettings.json).</summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    /// <summary>Listening interface.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// Game port. The client has it <b>hardcoded</b>: it ignores any port
    /// passed on the command line and always connects to 27050. Changing this
    /// breaks the connection with the original client.
    /// </summary>
    public int Port { get; set; } = 27050;

    /// <summary>
    /// Protocol revision announced during login. The 2019 client demands
    /// exactly 4578 and disconnects on any other value.
    /// </summary>
    public uint ProtocolRevision { get; set; } = 4578;

    /// <summary>Role bitmask handed to the player on entry.</summary>
    public uint DefaultRoles { get; set; } = 0;

    /// <summary>
    /// Development mode: accepts any session without validating it. Must be
    /// <c>false</c> once account persistence exists.
    /// </summary>
    public bool AllowAnyCredentials { get; set; } = true;

    /// <summary>Concurrent connections allowed.</summary>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>
    /// Send the player into the room once character creation finishes.
    /// </summary>
    /// <remarks>
    /// Was off while players had no ship: the hangar window reaches for the
    /// active one and, finding null, threw inside the client's <c>Update</c> —
    /// once per frame, instantiating the scenery each time, until memory ran
    /// out. Players now get one, so it is on.
    /// </remarks>
    public bool EnableRoomEntry { get; set; } = true;
}

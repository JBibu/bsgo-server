using Bsgo.Protocol;
using Bsgo.Server.Net;
using Bsgo.Server.Players;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsgo.Server.Protocols;

/// <summary>
/// Entry handshake. Sequence the client expects:
/// <code>
///   server -> Hello
///   client -> Init
///   server -> Init (protocol revision)
///   client -> Player (session credentials)
///   server -> Player (server time + roles)  |  Wait (queue)  |  Error
/// </code>
/// </summary>
public sealed class LoginProtocolHandler(
    IOptions<ServerOptions> options,
    IPlayerStore store,
    IEnumerable<IPlayerEnteredHook> enteredHooks,
    ILogger<LoginProtocolHandler> logger) : IProtocolHandler
{
    private readonly ServerOptions _options = options.Value;
    private readonly IPlayerEnteredHook[] _enteredHooks = [.. enteredHooks.OrderBy(h => h.Order)];

    public ProtocolId Protocol => ProtocolId.Login;

    public Task OnConnectedAsync(BgoConnection connection, CancellationToken ct)
    {
        logger.LogInformation("Client connected from {Endpoint}", connection.RemoteEndPoint);
        return connection.SendAsync(ProtocolId.Login, (ushort)LoginReply.Hello, ct);
    }

    public Task HandleAsync(BgoConnection connection, ushort messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => (LoginRequest)messageType switch
        {
            LoginRequest.Init => SendInitAsync(connection, ct),
            LoginRequest.Player => HandlePlayerAsync(connection, payload, ct),
            LoginRequest.Echo => Task.CompletedTask,
            _ => ProtocolLog.Unhandled<LoginRequest>(logger, connection, messageType),
        };

    /// <summary>
    /// Replies with the protocol revision. The client compares it against its
    /// own and drops the connection on a mismatch, so this number decides
    /// whether the game is playable at all.
    /// </summary>
    private Task SendInitAsync(BgoConnection connection, CancellationToken ct)
    {
        var w = new BgoWriter();
        w.Write(_options.ProtocolRevision);
        return connection.SendAsync(ProtocolId.Login, (ushort)LoginReply.Init, w, ct);
    }

    private async Task HandlePlayerAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var r = new BgoReader(payload.Span);
        var connectType = (ConnectType)r.ReadByte();
        uint playerId = r.ReadUInt32();
        string playerName = r.ReadString();
        string sessionCode = r.ReadString();

        logger.LogInformation(
            "Login: id={PlayerId} name={PlayerName} type={ConnectType}",
            playerId, playerName, connectType);

        // TODO: validate sessionCode against the account. Characters persist,
        // but there is nothing above them yet — no accounts to check a session
        // against — so the server takes any session (development mode).
        if (!_options.AllowAnyCredentials && string.IsNullOrEmpty(sessionCode))
        {
            await SendErrorAsync(connection, LoginError.WrongSession, ct);
            return;
        }

        // The client remembers the id the server gives it and sends it back
        // from then on; it sends 0 when it has none yet. Handing that 0 back
        // would make every player share the same character.
        if (playerId == 0)
        {
            playerId = await store.AllocatePlayerIdAsync(ct);
            logger.LogInformation(
                "Client arrived without an identifier; assigning {PlayerId}", playerId);
        }

        connection.State.PlayerId = playerId;
        connection.State.PlayerName = playerName;

        await SendPlayerAsync(connection, ct);

        // Everything the client expects to find waiting for it without asking:
        // its identifier, the avatar catalogue, the saved settings.
        var player = await store.GetOrCreateAsync(playerId, ct);
        foreach (var hook in _enteredHooks)
            await hook.OnPlayerEnteredAsync(connection, player, ct);
    }

    /// <summary>
    /// Confirms the login: server date broken into fields, a reference
    /// timestamp for clock sync, and the player's role bitmask.
    /// </summary>
    private Task SendPlayerAsync(BgoConnection connection, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var w = new BgoWriter();
        w.Write(now.Year);
        w.Write(now.Month);
        w.Write(now.Day);
        w.Write(now.Hour);
        w.Write(now.Minute);
        w.Write(now.Second);
        // Reference for clock synchronisation: milliseconds since the Unix
        // epoch (confirmed in the client, which builds the date as
        // 1970-01-01 + these milliseconds).
        w.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        w.Write(_options.DefaultRoles);
        return connection.SendAsync(ProtocolId.Login, (ushort)LoginReply.Player, w, ct);
    }

    private Task SendErrorAsync(BgoConnection connection, LoginError error, CancellationToken ct)
    {
        logger.LogWarning("Login rejected for {Endpoint}: {Error}", connection.RemoteEndPoint, error);
        var w = new BgoWriter();
        w.Write((byte)error);
        return connection.SendAsync(ProtocolId.Login, (ushort)LoginReply.Error, w, ct);
    }

}


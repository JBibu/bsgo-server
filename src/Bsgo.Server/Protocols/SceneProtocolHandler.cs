using Bsgo.Protocol;
using Bsgo.Server.Net;
using Bsgo.Server.Scenes;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Protocols;

/// <summary>
/// Controls which scene the client is in. The server decides where it goes: the
/// client asks to leave the login and waits to be told what to load.
/// </summary>
public sealed class SceneProtocolHandler(
    SceneDirector scenes,
    ILogger<SceneProtocolHandler> logger) : IProtocolHandler
{
    public ProtocolId Protocol => ProtocolId.Scene;

    public Task HandleAsync(BgoConnection connection, ushort messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => (SceneRequest)messageType switch
        {
            SceneRequest.QuitLogin => SendNextSceneAsync(connection, ct),
            SceneRequest.SceneLoaded => OnSceneLoaded(connection),
            SceneRequest.Disconnect => OnDisconnectRequested(connection),
            SceneRequest.StopDisconnect => Task.CompletedTask,
            _ => ProtocolLog.Unhandled<SceneRequest>(logger, connection, messageType),
        };

    /// <summary>
    /// The client has left the login and is waiting for a destination.
    /// </summary>
    /// <remarks>
    /// TODO: once persistence exists, choose here between character creation
    /// and resuming the saved game (Room/Space).
    /// </remarks>
    private Task SendNextSceneAsync(BgoConnection connection, CancellationToken ct)
        => scenes.SendToFactionSelectionAsync(connection, ct);

    private Task OnSceneLoaded(BgoConnection connection)
    {
        logger.LogInformation("Scene loaded by {Endpoint}", connection.RemoteEndPoint);
        return Task.CompletedTask;
    }

    private Task OnDisconnectRequested(BgoConnection connection)
    {
        logger.LogInformation("Client {Endpoint} requested a disconnect", connection.RemoteEndPoint);
        return Task.CompletedTask;
    }

}

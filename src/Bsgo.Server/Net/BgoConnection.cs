using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using Bsgo.Protocol;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Net;

/// <summary>
/// A client connection: decodes messages off the socket and dispatches them to
/// the matching protocol handler.
/// </summary>
/// <remarks>
/// Uses <see cref="System.IO.Pipelines"/> to avoid copying buffers and to keep
/// from hand-managing messages split across TCP packets.
/// </remarks>
public sealed class BgoConnection(
    Socket socket,
    IReadOnlyDictionary<ProtocolId, IProtocolHandler> handlers,
    ILogger<BgoConnection> logger)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>Per-session state that handlers read and modify.</summary>
    public SessionState State { get; } = new();

    public string RemoteEndPoint { get; } = socket.RemoteEndPoint?.ToString() ?? "unknown";

    public async Task RunAsync(CancellationToken ct)
    {
        var reader = PipeReader.Create(new NetworkStream(socket, ownsSocket: false));
        try
        {
            // The server speaks first: the client expects Hello on connect.
            if (handlers.TryGetValue(ProtocolId.Login, out var login))
                await login.OnConnectedAsync(this, ct);

            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                while (BgoFraming.TryReadFrame(ref buffer, out var header, out var payload))
                    await DispatchAsync(header, payload, ct);

                // Consumed up to `buffer`, examined everything received, so an
                // incomplete message waits for more bytes to arrive.
                reader.AdvanceTo(buffer.Start, result.Buffer.End);

                if (result.IsCompleted) break;
            }
        }
        catch (BgoProtocolException ex)
        {
            logger.LogWarning("Invalid protocol from {Endpoint}: {Message}", RemoteEndPoint, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            logger.LogDebug("Connection with {Endpoint} closed: {Message}", RemoteEndPoint, ex.Message);
        }
        finally
        {
            await reader.CompleteAsync();
            socket.Dispose();
        }
    }

    private async Task DispatchAsync(BgoMessageHeader header, ReadOnlySequence<byte> payload, CancellationToken ct)
    {
        if (!handlers.TryGetValue(header.Protocol, out var handler))
        {
            logger.LogWarning(
                "No handler for protocol {Protocol} (message {Type}) from {Endpoint}",
                header.Protocol, header.MessageType, RemoteEndPoint);
            return;
        }

        // The payload may span several pipe segments; flattening it lets
        // BgoReader work over a contiguous span.
        var bytes = payload.IsSingleSegment ? payload.First : payload.ToArray();
        await handler.HandleAsync(this, header.MessageType, bytes, ct);
    }

    /// <summary>Sends an already serialised message, framing it.</summary>
    public async Task SendAsync(ProtocolId protocol, ushort messageType, BgoWriter payload, CancellationToken ct)
    {
        var frame = BgoFraming.Frame(protocol, messageType, payload);

        // A socket does not allow concurrent sends: the bytes would interleave.
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(frame, SocketFlags.None, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Sends a message with no payload.</summary>
    public Task SendAsync(ProtocolId protocol, ushort messageType, CancellationToken ct) =>
        SendAsync(protocol, messageType, new BgoWriter(0), ct);

}

/// <summary>Session data shared between protocol handlers.</summary>
public sealed class SessionState
{
    /// <summary>Identifier of the player on this connection; 0 before login.</summary>
    public uint PlayerId { get; set; }

    public string PlayerName { get; set; } = string.Empty;
}

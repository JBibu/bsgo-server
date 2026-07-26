using Bsgo.Protocol;
using Bsgo.Server.Net;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Protocols;

/// <summary>
/// Clock synchronisation. The client asks for the server time periodically and
/// estimates latency from the round trip.
/// </summary>
/// <remarks>
/// The value is <b>milliseconds since the Unix epoch</b>. The reply should be
/// produced as late as possible — right as it is written — because any delay
/// added here reads to the client as clock drift.
/// </remarks>
public sealed class SyncProtocolHandler(ILogger<SyncProtocolHandler> logger) : IProtocolHandler
{
    public ProtocolId Protocol => ProtocolId.Sync;

    public Task HandleAsync(BgoConnection connection, ushort messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if ((SyncRequest)messageType != SyncRequest.SyncRequest)
            return ProtocolLog.Unhandled<SyncRequest>(logger, connection, messageType);

        var w = new BgoWriter(8);
        w.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return connection.SendAsync(ProtocolId.Sync, (ushort)SyncReply.SyncReply, w, ct);
    }
}

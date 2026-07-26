using Bsgo.Protocol;

namespace Bsgo.Server.Net;

/// <summary>
/// Handles the messages of one <see cref="ProtocolId"/>. One handler per
/// protocol; the dispatcher picks it from the first byte of the body.
/// </summary>
public interface IProtocolHandler
{
    ProtocolId Protocol { get; }

    /// <summary>Processes an incoming message from the client.</summary>
    /// <param name="messageType">Member of this protocol's <c>Request</c> enum.</param>
    /// <param name="payload">Message body, header already stripped.</param>
    Task HandleAsync(BgoConnection connection, ushort messageType, ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>
    /// Called once the connection is established, before anything is received.
    /// Lets the server speak first (the client waits for <c>Hello</c>).
    /// </summary>
    Task OnConnectedAsync(BgoConnection connection, CancellationToken ct) => Task.CompletedTask;
}

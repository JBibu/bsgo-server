using Bsgo.Protocol;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Net;

/// <summary>
/// Pushes data to a client right after it authenticates.
/// </summary>
/// <remarks>
/// The client expects several things to be waiting for it on entry (its
/// identifier, the avatar catalogue, saved settings) without asking for any of
/// them. Without this, the login handler would have to depend on each concrete
/// handler that owns one of those messages, and every new one would add a
/// constructor parameter plus a second registration in the container.
/// </remarks>
public interface IPlayerEnteredHook
{
    /// <summary>
    /// Relative order, lowest first. It matters: the client builds a default
    /// avatar from the catalogue when the faction reply arrives, so the
    /// catalogue has to be on the wire before the player can pick one.
    /// </summary>
    int Order => 100;

    Task OnPlayerEnteredAsync(BgoConnection connection, CancellationToken ct);
}

/// <summary>Shared logging for messages a protocol does not implement yet.</summary>
public static class ProtocolLog
{
    /// <summary>
    /// Logs an unhandled message, resolving the numeric type to its name.
    /// </summary>
    /// <remarks>
    /// The name matters when reading logs: <c>Player</c> alone has 46 distinct
    /// requests, and a bare number says nothing about what the client wanted.
    /// </remarks>
    public static Task Unhandled<TRequest>(ILogger logger, BgoConnection connection, ushort messageType)
        where TRequest : struct, Enum
    {
        var request = (TRequest)Enum.ToObject(typeof(TRequest), messageType);
        var name = Enum.IsDefined(request) ? request.ToString() : "unknown";

        logger.LogWarning(
            "Unimplemented {Protocol} request: {Name} ({Type}) from {Endpoint}",
            typeof(TRequest).Name.Replace("Request", string.Empty),
            name, messageType, connection.RemoteEndPoint);
        return Task.CompletedTask;
    }
}

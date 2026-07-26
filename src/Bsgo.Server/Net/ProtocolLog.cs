using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Net;

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

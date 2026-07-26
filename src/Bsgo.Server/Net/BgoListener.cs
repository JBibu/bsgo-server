using System.Net;
using System.Net.Sockets;
using Bsgo.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsgo.Server.Net;

/// <summary>
/// Accepts TCP connections and spawns one <see cref="BgoConnection"/> per client.
/// </summary>
public sealed class BgoListener(
    IOptions<ServerOptions> options,
    IEnumerable<IProtocolHandler> handlers,
    ILoggerFactory loggerFactory,
    ILogger<BgoListener> logger) : BackgroundService
{
    private readonly ServerOptions _options = options.Value;
    private readonly IReadOnlyDictionary<ProtocolId, IProtocolHandler> _handlers =
        handlers.ToDictionary(h => h.Protocol);

    private int _connectionCount;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_options.ListenAddress), _options.Port);
        using var listener = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(endpoint);
        listener.Listen(backlog: 128);

        logger.LogInformation(
            "BSGO server listening on {Endpoint} (protocol revision {Revision}, {Handlers} protocols registered)",
            endpoint, _options.ProtocolRevision, _handlers.Count);

        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (Interlocked.Increment(ref _connectionCount) > _options.MaxConnections)
            {
                Interlocked.Decrement(ref _connectionCount);
                logger.LogWarning("Connection limit of {Max} reached; rejecting client", _options.MaxConnections);
                client.Dispose();
                continue;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        client.NoDelay = true;   // the game sends small, frequent messages
        var connection = new BgoConnection(client, _handlers, loggerFactory.CreateLogger<BgoConnection>());
        try
        {
            await connection.RunAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled failure on connection {Endpoint}", connection.RemoteEndPoint);
        }
        finally
        {
            Interlocked.Decrement(ref _connectionCount);
        }
    }
}

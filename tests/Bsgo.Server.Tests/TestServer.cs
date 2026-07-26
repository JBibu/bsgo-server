using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Bsgo.Protocol;
using Bsgo.Server.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Tests;

/// <summary>
/// Starts a real server on a free port, with the same services as production.
/// </summary>
/// <remarks>
/// Centralised on purpose: when each test built its own host, a new dependency
/// in any handler broke every file at once.
/// </remarks>
public sealed class TestServer : IAsyncDisposable
{
    private readonly IHost _host;

    public int Port { get; }
    public IServiceProvider Services => _host.Services;

    private TestServer(IHost host, int port)
    {
        _host = host;
        Port = port;
    }

    public static async Task<TestServer> StartAsync(Action<ServerOptions>? configure = null)
    {
        int port = GetFreePort();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<ServerOptions>(o =>
        {
            o.ListenAddress = "127.0.0.1";
            o.Port = port;
            configure?.Invoke(o);
        });

        // The very same registrations the real server uses.
        builder.Services.AddBsgoServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var host = builder.Build();
        await host.StartAsync();
        return new TestServer(host, port);
    }

    /// <summary>Opens a client connection to the server.</summary>
    public async Task<TestClient> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, Port);
        return new TestClient(client);
    }

    private static int GetFreePort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>Test client that speaks the real byte format.</summary>
public sealed class TestClient(TcpClient client) : IDisposable
{
    private readonly NetworkStream _stream = client.GetStream();

    public int Available => client.Available;

    /// <summary>Reads a full message.</summary>
    /// <remarks>
    /// Decoded with <see cref="BgoFraming"/> rather than by hand: the mixed
    /// endianness of the header is exactly the trap the protocol documents, and
    /// a second implementation here would let the tests keep passing while the
    /// real framing changed underneath them.
    /// </remarks>
    public async Task<(ProtocolId Protocol, ushort Type, byte[] Payload)> ReadAsync()
    {
        var prefix = new byte[BgoFraming.LengthPrefixSize];
        await _stream.ReadExactlyAsync(prefix);

        var frame = new byte[BgoFraming.LengthPrefixSize + BinaryPrimitives.ReadUInt16BigEndian(prefix)];
        prefix.CopyTo(frame, 0);
        await _stream.ReadExactlyAsync(frame.AsMemory(BgoFraming.LengthPrefixSize));

        var input = new ReadOnlySequence<byte>(frame);
        if (!BgoFraming.TryReadFrame(ref input, out var header, out var payload))
            throw new InvalidOperationException("the server sent an incomplete frame");

        return (header.Protocol, header.MessageType, payload.ToArray());
    }

    /// <summary>Reads messages until the wanted type shows up, discarding the rest.</summary>
    /// <remarks>
    /// The server sends several things in a row on entry (identifier,
    /// catalogue, settings), and a test that only looks at the next message
    /// turns brittle every time one more is added.
    /// </remarks>
    public async Task<(ProtocolId Protocol, ushort Type, byte[] Payload)> ReadUntilAsync(
        ProtocolId protocol, ushort type, int maxMessages = 20)
    {
        for (int i = 0; i < maxMessages; i++)
        {
            var message = await ReadAsync();
            if (message.Protocol == protocol && message.Type == type)
                return message;
        }

        throw new TimeoutException(
            $"message {type} of protocol {protocol} did not arrive within {maxMessages} messages");
    }

    public Task SendAsync(ProtocolId protocol, ushort type, BgoWriter payload)
        => _stream.WriteAsync(BgoFraming.Frame(protocol, type, payload)).AsTask();

    public Task SendAsync(ProtocolId protocol, ushort type)
        => SendAsync(protocol, type, new BgoWriter(0));

    /// <summary>Writes loose bytes, to exercise split messages.</summary>
    public async Task SendRawAsync(byte[] bytes)
    {
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();
    }

    public void Dispose() => client.Dispose();
}

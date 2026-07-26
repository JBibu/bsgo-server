using Bsgo.Protocol;
using Bsgo.Server.Net;
using Bsgo.Server.Players;
using Microsoft.Extensions.Logging;

namespace Bsgo.Server.Protocols;

/// <summary>
/// Player settings: game options and key bindings.
/// </summary>
/// <remarks>
/// The contents are an opaque blob only the client interprets. The server
/// stores it verbatim and hands it back, with no need to know its structure:
/// the effect for the player is the same, and it avoids depending on an
/// internal format that changes between client versions.
/// </remarks>
public sealed class SettingProtocolHandler(
    IPlayerStore store,
    ILogger<SettingProtocolHandler> logger) : IProtocolHandler, IPlayerEnteredHook
{
    public ProtocolId Protocol => ProtocolId.Setting;

    public Task HandleAsync(BgoConnection connection, ushort messageType, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => (SettingRequest)messageType switch
        {
            SettingRequest.SaveSettings => SaveAsync(connection, payload, isKeys: false),
            SettingRequest.SaveKeys => SaveAsync(connection, payload, isKeys: true),
            SettingRequest.SetSyfyShip => Task.CompletedTask,
            SettingRequest.SetFullScreen => Task.CompletedTask,
            _ => ProtocolLog.Unhandled<SettingRequest>(logger, connection, messageType),
        };

    private Task SaveAsync(BgoConnection connection, ReadOnlyMemory<byte> payload, bool isKeys)
    {
        var player = store.GetOrCreate(connection.State.PlayerId);
        if (isKeys)
            player.KeyBindings = payload.ToArray();
        else
            player.Settings = payload.ToArray();
        store.Save(player);

        logger.LogDebug(
            "Stored {Bytes} bytes of {What} for player {PlayerId}",
            payload.Length, isKeys ? "key bindings" : "settings", player.Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the saved settings on entry so the client restores its
    /// configuration; if nothing is stored, nothing is sent and the client
    /// falls back to its defaults.
    /// </summary>
    public async Task OnPlayerEnteredAsync(BgoConnection connection, CancellationToken ct)
    {
        var player = store.GetOrCreate(connection.State.PlayerId);

        if (player.Settings.Length > 0)
        {
            var w = new BgoWriter(player.Settings.Length);
            w.WriteRaw(player.Settings);
            await connection.SendAsync(ProtocolId.Setting, (ushort)SettingReply.Settings, w, ct);
        }

        if (player.KeyBindings.Length > 0)
        {
            var w = new BgoWriter(player.KeyBindings.Length);
            w.WriteRaw(player.KeyBindings);
            await connection.SendAsync(ProtocolId.Setting, (ushort)SettingReply.Keys, w, ct);
        }
    }

}

using Bsgo.Server.Catalogue;
using Bsgo.Server.Net;
using Bsgo.Server.Players;
using Bsgo.Server.Protocols;
using Bsgo.Server.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Bsgo.Server;

/// <summary>
/// The server's composition root.
/// </summary>
/// <remarks>
/// Lives here rather than in <c>Program</c> so the tests spin up the very same
/// set of services. When the two lists were maintained separately, registering
/// a handler in one and forgetting the other left the tests passing against a
/// composition the real server never had.
/// </remarks>
public static class ServerServices
{
    /// <summary>Where the generated game data files live, relative to the binary.</summary>
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    public static string DataFile(string name) => Path.Combine(DataDirectory, name);

    /// <param name="connectionString">
    /// Where the characters live. Given one, the server persists to Postgres;
    /// without it, everything stays in memory and is lost on restart — which is
    /// what the tests want, and the only way to run without a database at hand.
    /// </param>
    public static IServiceCollection AddBsgoServer(
        this IServiceCollection services, string? connectionString = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IPlayerStore, InMemoryPlayerStore>();
        }
        else
        {
            services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
            services.AddSingleton<IPlayerStore, PostgresPlayerStore>();

            // Before the listener on purpose: hosted services start in the
            // order they are registered, and no client may reach a server whose
            // tables do not exist yet.
            services.AddHostedService<PlayerSchema>();
        }

        // Static game data, generated from the client's assets.
        services.AddSingleton(_ => AvatarCatalogue.LoadFrom(DataFile("avatar-catalogue.json")));
        services.AddSingleton(_ => RoomCatalogue.LoadFrom(DataFile("rooms.json")));
        services.AddSingleton(_ => ShipCatalogue.LoadFrom(DataFile("ships.json")));

        // Card sources. A new kind of card is a provider registered here, with
        // no changes to the catalogue handler.
        services.AddSingleton<ICardProvider, AvatarCardProvider>();
        services.AddSingleton<ICardProvider, RoomCardProvider>();
        services.AddSingleton<ICardProvider, ShipCardProvider>();

        services.AddSingleton<SceneDirector>();

        // The hangar both owns the player's ship and pushes it on entry.
        services.AddSingleton<Hangar>();
        services.AddSingleton<IPlayerEnteredHook>(sp => sp.GetRequiredService<Hangar>());

        services.AddProtocolHandler<LoginProtocolHandler>();
        services.AddProtocolHandler<SyncProtocolHandler>();
        services.AddProtocolHandler<SceneProtocolHandler>();
        services.AddProtocolHandler<PlayerProtocolHandler>();
        services.AddProtocolHandler<SettingProtocolHandler>();
        services.AddProtocolHandler<CatalogueProtocolHandler>();

        services.AddHostedService<BgoListener>();
        return services;
    }

    /// <summary>
    /// Registers a handler once and exposes it under every role it implements.
    /// </summary>
    /// <remarks>
    /// A handler that also pushes data on entry must be the same instance in
    /// both roles, hence the single registration plus forwarding.
    /// </remarks>
    private static void AddProtocolHandler<T>(this IServiceCollection services)
        where T : class, IProtocolHandler
    {
        services.AddSingleton<T>();
        services.AddSingleton<IProtocolHandler>(sp => sp.GetRequiredService<T>());

        if (typeof(IPlayerEnteredHook).IsAssignableFrom(typeof(T)))
            services.AddSingleton(sp => (IPlayerEnteredHook)sp.GetRequiredService<T>());
    }
}

# Working on bsgo-server

A server emulator for *Battlestar Galactica Online*, a game Bigpoint shut down
in 2019. The client still exists; the server does not. This project rebuilds it
by deriving the protocol from the client and implementing it from scratch.

## Ground rules

**Never commit game files.** The client binaries, its decompilation and its
assetbundles are Bigpoint's. They live in `client-ref/`, which is git-ignored
and excluded from Docker builds. What the repo may contain is *derived
specification* (message ids, byte layouts, asset names needed to interoperate)
and our own implementation.

**Everything runs in Docker.** The .NET SDK, the ILSpy decompiler and the Python
tooling live in the `toolchain` image. Do not install them on the host.

```bash
docker compose exec toolchain dotnet test     # 81 tests
docker compose exec toolchain dotnet build
docker compose up -d server                   # listens on 27050
docker compose logs -f server
```

## How this codebase is worked on

The protocol was not documented anywhere; it was recovered one screen at a time
by the same loop, which still applies:

1. Implement what the client last asked for.
2. Run the client against the server and read **both** logs. The server's say
   what arrived; the client's (`bsgo_Data/output_log.txt` inside the Wine
   prefix) say why it did nothing with it.
3. The warnings name the next thing to build.

`tools/run-client.sh` launches the real client against the local server.

**The client's log is the primary diagnostic tool.** Every hard bug in this
project was found there, never by staring at the server. A silent client is not
a mystery: it has an exception with a stack trace.

## Invariants that will bite you

These were each found the hard way. Breaking them produces **no error** — just a
client that hangs, or draws nothing.

- **The framing length prefix is big-endian.** Everything else in the protocol,
  including the message type right after it, is little-endian. Get it wrong and
  the client waits forever for a message that never completes.
- **Protocol revision `4578`.** The client compares it in the second handshake
  message and disconnects on a mismatch.
- **Port `27050` is hardcoded in the client.** `+gameServer` only sets the IP.
- **Every action that completes a screen must end in a scene transition.** The
  client never advances by itself; it sits on "Please wait" until the server
  tells it where to go. Confirming the action is not enough.
- **Asset extensions are not uniform.** The client appends `.prefab` itself, so
  prefab names must not carry it; materials need `.mat`, textures `.tga`/`.png`.
  A wrong name draws nothing and logs nothing on the server.
- **Card and catalogue keys are looked up by exact name.** A missing key throws
  `KeyNotFoundException` inside the client while reading the card.
- **Payload field order is the whole contract.** There are no tags, so one field
  too many or too few shifts everything after it. Tests re-read payloads field
  by field for this reason.

## Layout

```
spec/       protocol.json (generated) + wire-format.md (hand-written)
tools/      generators: protocol spec -> C# enums; client assets -> game data
data/       generated game data (avatar pieces, rooms)
src/Bsgo.Protocol/   wire format and framing, no server dependencies
src/Bsgo.Server/     listener, sessions, one handler per protocol
tests/               wire tests byte by byte; protocol flows against a real server
```

**The protocol is generated, not written.** 445 message types across 25
protocols come from `spec/protocol.json`. Never hand-edit `Generated/*.g.cs`.
Enums transcribed by hand into `src/Bsgo.Server` (`CardView`, `GameLocation`,
`AvatarItem`…) have already drifted from the client once — check them against
`client-ref/decompiled/` when touching them.

## Extension points

Adding to the server should not mean editing a dispatcher:

- **New protocol**: implement `IProtocolHandler`, register with
  `AddProtocolHandler<T>()` in `ServerServices`.
- **New catalogue card**: implement `ICardProvider`, register it. The catalogue
  handler stays untouched.
- **Data pushed on login**: implement `IPlayerEnteredHook` and give it an
  `Order`. Order matters — the avatar catalogue must reach the client before the
  faction reply.
- **Composition lives in `ServerServices.AddBsgoServer()`**, used by both the
  server and the tests, so they cannot drift apart.

## Current state

Working: login, character creation (faction, avatar, name) against the real
client.

Not working, and why:

- **Room entry is disabled** (`ServerOptions.EnableRoomEntry`). The hangar
  window reads the player's active ship; there are no ships. Being null it
  throws inside the client's `Update`, which retries every frame and
  instantiates the scenery once per attempt until it runs out of memory.
- **Everything is in memory.** The `db` container (Postgres) is up and unused.
- **Sessions are not validated.** Any client is accepted as any player.

## The wall ahead

Avatar piece names could be *recovered* — they are the names of meshes in the
client's assetbundles. Ship stats, sector layouts and item tables cannot: they
lived on Bigpoint's server and are preserved nowhere. Implementing ships means
**inventing** those values, at which point the project stops reconstructing the
original game and starts defining its own. That is a design decision for the
maintainer, not something to slip in while implementing a protocol.

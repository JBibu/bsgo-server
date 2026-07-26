# Working on bsgo-server

See `README.md` for what this project is and where it stands.

## Rules

- **Never commit game files.** The client binaries, their decompilation and the
  assetbundles are Bigpoint's. They live in `client-ref/`, git-ignored and out
  of Docker builds. The repo holds derived specification and our own code.
- **Everything runs in Docker.** Do not install the .NET SDK, ILSpy or the
  Python tooling on the host.
- **Never hand-edit `src/Bsgo.Protocol/Generated/*.g.cs`.** Regenerate instead.

```bash
docker compose exec toolchain dotnet test    # needs the db container up
docker compose exec toolchain dotnet build
docker compose up -d server                  # listens on 27050
docker compose logs -f server
tools/run-client.sh                          # the real client against the local server
```

## Debugging

Read **both** logs. The server's says what arrived; the client's has the
exception — `bsgo_Data/output_log.txt` inside the Wine prefix.

## Invariants

- **The framing length prefix is big-endian.** Everything after it, including
  the message type, is little-endian.
- **Protocol revision `4578`.** A mismatch disconnects the client.
- **Port `27050` is hardcoded in the client.** `+gameServer` only sets the IP.
- **Every action that completes a screen must end in a scene transition**, or
  the client sits on "Please wait" indefinitely. Confirming the action is not
  enough.
- **Asset extensions are not uniform.** Prefab names must not carry `.prefab`,
  the client appends it; materials need `.mat`, textures `.tga`/`.png`.
- **Card and catalogue keys are looked up by exact name.** A missing one throws
  `KeyNotFoundException` inside the client.
- **Payload field order is the whole contract.** There are no tags: one field
  too many or too few shifts everything after it.
- **Do not enable `ServerOptions.EnableRoomEntry` until players have a ship.**

## Layout

```
spec/       protocol.json (generated) + wire-format.md (hand-written)
tools/      generators: protocol spec -> C# enums; client assets -> game data
data/       avatar pieces and rooms (generated), ships (hand-edited)
src/Bsgo.Protocol/   wire format and framing, no server dependencies
src/Bsgo.Server/     listener, sessions, one handler per protocol
tests/               wire tests byte by byte; protocol flows against a real server
```

## Extension points

- **New protocol**: implement `IProtocolHandler`, register with
  `AddProtocolHandler<T>()`.
- **New catalogue card**: implement `ICardProvider`, register it.
- **Data pushed on login**: implement `IPlayerEnteredHook` with an `Order`. The
  avatar catalogue must reach the client before the faction reply.
- **Composition**: `ServerServices.AddBsgoServer()`, shared by the server and
  the tests. A connection string wires Postgres, none wires the in-memory store.

## Regenerating the protocol

```bash
docker compose exec toolchain python3 tools/extract_protocol_spec.py \
    client-ref/decompiled spec/protocol.json
docker compose exec toolchain python3 tools/generate_protocol_cs.py \
    spec/protocol.json src/Bsgo.Protocol/Generated
```

The client declares the 7 shared enums as plain `enum`, so their wire width is
not in it: that table is `SHARED_ENUMS` in the extractor.

## Editing data/ships.json

Hand-edited. `ShipTableTests` enforces its rules — run the tests after changing
a value. It only carries stats the client has in `ObjectStat`; anything else is
noise the server can never send.

# bsgo-server

A server emulator for *Battlestar Galactica Online*, an MMO Bigpoint shut down
in 2019. The client still exists; the server does not. This rebuilds it in
C# / .NET 9, against a protocol specification derived by interoperability
analysis of the client.

> **Not affiliated with Bigpoint or its subsidiaries.** *Battlestar Galactica*
> and all related marks belong to their respective owners. This is a
> preservation project for a discontinued game.
>
> **No game files are distributed here.** You need your own copy of the client.
> The repository contains only a derived interface specification and an
> independent implementation.

## Status

| Component | State |
|---|---|
| Protocol specification (25 protocols, 445 messages) | Extracted and versioned |
| Wire format (read/write, framing) | Implemented, verified byte by byte |
| Generated C# message enums | Generated from the spec |
| Login handshake | Working end to end |
| Character creation (faction, avatar, name) | Working against the real client |
| Room entry | Working against the real client |
| Persistence | Characters in Postgres; no accounts yet |
| Ships | All 64 served as catalogue cards; players get their faction's starter |

## Layout

```
spec/
  protocol.json         protocol and message ids (generated)
  wire-format.md        byte encoding, written by hand
tools/
  extract_protocol_spec.py     decompiled client -> spec/protocol.json
  generate_protocol_cs.py      spec/protocol.json  -> C# enums
  generate_avatar_catalogue.py client assetbundles -> data/avatar-catalogue.json
  run-client.sh                launches the client against the local server
data/
  avatar-catalogue.json avatar pieces (generated from the client's assets)
  rooms.json            playable rooms
  ships.json            the 64 ships and their stats
src/
  Bsgo.Protocol/        wire format, framing, generated enums
  Bsgo.Server/          TCP listener, sessions, protocol handlers
tests/
  Bsgo.Protocol.Tests/  the wire, byte by byte
  Bsgo.Server.Tests/    protocol flows against a real server
client-ref/             client files (git-ignored, never published)
```

## Requirements

Only **Docker**. Nothing is installed on the host: the .NET SDK, the decompiler
and the Python tooling all live inside the `toolchain` image.

## Usage

Start the server:

```bash
docker compose up -d server
docker compose logs -f server
```

It listens on port `27050`, published on loopback only. The client also uses
`27051` for Unity's socket policy.

Work on the code (build, tests, generators):

```bash
docker compose up -d toolchain
docker compose exec toolchain dotnet test
docker compose exec toolchain dotnet build
```

### Connecting the client

```bash
tools/run-client.sh
```

With `AllowAnyCredentials` on (the default) the server accepts any session; it
is a development mode and must be turned off once real accounts exist.

## Regenerating the protocol

Requires the client files in `client-ref/`. Decompilation is a local analysis
step: its output is never published.

```bash
docker compose exec toolchain bash -c '
  ilspycmd -o client-ref/decompiled -p \
    client-ref/client/live/bsgo_Data/Managed/Assembly-CSharp.dll
  python3 tools/extract_protocol_spec.py client-ref/decompiled spec/protocol.json
  python3 tools/generate_protocol_cs.py  spec/protocol.json src/Bsgo.Protocol/Generated'
```

## Design notes

**The protocol is generated, not written.** There are 445 message types, plus
the enums that travel inside them; by hand they drift out of sync.
`spec/protocol.json` is the single source of truth.

**The protocol revision is `4578`.** The client compares it against its own in
the second handshake message and drops the connection on a mismatch.

**Port `27050` is hardcoded in the client.** The `+gameServer` argument only
sets the IP; the port cannot be changed.

**Lengths are `u16`, not LEB128.** The client overrides `BinaryWriter`'s default
behaviour, so `BinaryWriter.Write(string)` cannot be used as-is. See
`spec/wire-format.md`.

**The framing length prefix is big-endian**, unlike everything else in the
protocol. Get it wrong and the client waits forever with no error.

**Asset extensions are not uniform.** The client appends `.prefab` itself, so
prefab names must not carry it; materials need `.mat` and textures `.tga`/`.png`.
A wrong name produces no error, just an invisible object.

**`Euler3` is pitch/yaw/roll**, not x/y/z. Converting to a quaternion without
respecting Unity's rotation order misaligns ship orientation; it is the
likeliest source of error in the whole movement layer.

**Every action that completes a screen must end in a scene transition.** The
client never advances on its own: it waits on "Please wait" until the server
tells it where to go.

**`+cdn` cannot contain spaces.** The client concatenates several arguments into
one string and splits on spaces; a path like `Program Files (x86)` throws the
parse out of step and the client refuses to start. `tools/run-client.sh` works
around it with a `C:\bsgo` link.

## Known limitations

**The shop, chat and several catalogue cards are not implemented.** The room
loads and can be walked out of, but opening the shop puts the client in a
throw-per-frame loop of its own — it asks a protocol nothing answers and then
reads the reply it never got. Chat does the same on Enter. Neither touches the
room itself.

**There are no accounts.** Characters are stored in Postgres and survive a
restart, but nothing above them is: the login believes whatever identifier the
client offers, so sessions are not validated and any client is accepted as any
player.

**Weapon, system and sector data do not exist.** Ship stats do — `data/ships.json`
has all 64 with their full stat block — but the tables around them were the
original server's and are not preserved. A server with ships and no weapons is
not playable, so those values would have to be invented.

## Contributing

Issues and pull requests are welcome. Two things worth knowing before starting:

- `CLAUDE.md` documents the invariants that produce **silent** failures — a
  hanging client rather than an error. Reading it first saves a lot of time.
- The client's own log (`bsgo_Data/output_log.txt` in the Wine prefix) is the
  primary diagnostic tool. Every hard bug here was found there.

## Licence

[GNU AGPL-3.0](LICENSE). This is server software: if you run a modified version
of it as a public service, the licence requires you to publish your changes.

## Credits

Prior community work documenting the game:
[victti/BSGO-Private-Server](https://github.com/victti/BSGO-Private-Server) and
the README of [victti/OpenBSGO](https://github.com/victti/OpenBSGO).

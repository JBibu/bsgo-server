# BSGO wire format

Specification derived by interoperability analysis of the client (`bsgo.exe`,
2019-01-01 build). It describes **how the bytes are encoded** so we can talk to
the client; it contains no client code.

All integers are **little-endian** (the native format of .NET's
`BinaryWriter`). Floats are 32-bit IEEE-754.

## Transport

TCP, a single socket, **no encryption and no cryptographic handshake**. Port
`P+1` is used for the Flash/Unity socket policy (`PrefetchSocketPolicy`).

## Framing

```
+----------------+------------------+-----------------+-----------------+
| length : u16   | protocolID : u8  | msgType : u16   | payload : bytes |
+----------------+------------------+-----------------+-----------------+
 \__ length header __/ \__________ message body (length bytes) __________/
```

The client's reader is a two-state machine (`isReadLength` → `packetLength`):
first it consumes 2 length bytes, then exactly that many bytes. `protocolID`
selects the handler (25 values, see `protocol.json`); `msgType` is the member of
that protocol's `Request` (client→server) or `Reply` (server→client) enum.

`length` counts **the body only**: it does not include its own 2 bytes.

### The length prefix is big-endian

**This is the only exception in the entire protocol**, and nothing anywhere
signals it. Those 2 bytes go most-significant-byte first; everything else —
including the `msgType` that comes right after — is little-endian.

Writing it little-endian produces no visible error: the client simply reads a
nonsensical size (a 3-byte message becomes a 768-byte one) and blocks waiting
for data that never arrives, showing "Connecting..." indefinitely.

## Primitive types

| Type | Encoding |
|---|---|
| `bool` | 1 byte (0 / 1) |
| `byte` / `sbyte` | 1 byte |
| `u16` / `i16` | 2 bytes LE |
| `u32` / `i32` | 4 bytes LE |
| `u64` / `i64` | 8 bytes LE |
| `float` | 4 bytes IEEE-754 LE |

## Length prefixes

**Every** length (strings, arrays, lists, sets, compressed blocks) is encoded as
a **`u16`**, not as the 7-bit compressed integer `BinaryWriter`/`BinaryReader`
use by default. The client overrides that behaviour, so a .NET implementation
**cannot** use `BinaryWriter.Write(string)` as-is.

## Composite types

| Type | Encoding |
|---|---|
| `string` | `u16` length **in bytes** + those bytes as UTF-8. Length 0 → empty string, no bytes |
| `byte[]` | `u16` count + the bytes |
| `string[]` | `u16` count + each string |
| `List<T>` / `T[]` / `HashSet<T>` | `u16` count + each element |
| `Vector2` | 2 floats: `x`, `y` |
| `Vector3` | 3 floats: `x`, `y`, `z` |
| `Euler3` | 3 floats: `pitch`, `yaw`, `roll` |
| `Quaternion` | 4 floats: `x`, `y`, `z`, `w` |
| `Color` | 4 bytes: `r`, `g`, `b`, `a`, each channel as `(byte)(f * 255)` |
| `Tick` | `i32` |
| `GUID` | `u32` |

### Descriptors

Types implementing `IProtocolRead` / `IProtocolWrite` are serialised by calling
their own method: they carry no header and no type tag. The field order **is**
the contract. `Tick` is the minimal example (a single `i32`).

### Compressed blocks

Some large payloads are compressed: `u16` length + **zlib** data (zlib header,
not raw deflate). They decompress into a fresh buffer that is then read with the
same rules.

## Semantics worth knowing

`Euler3` stores **pitch/yaw/roll**, not `x/y/z`: converting it to a quaternion
requires respecting Unity's rotation order or ship orientation will diverge from
the client. It is the likeliest source of error in the whole movement layer.

#!/usr/bin/env python3
"""Extracts the BSGO protocol specification from the decompiled client.

Output: spec/protocol.json — an *interface* inventory (protocol and message
ids) derived from the client, not code. It is the basis for implementing the
server from scratch in any language.

Usage (inside the container):
    python3 tools/extract_protocol_spec.py client-ref/decompiled spec/protocol.json
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

# `public enum Name : type {` ... `}` — captures the enum body.
ENUM_RE = re.compile(
    r"public\s+enum\s+(?P<name>\w+)\s*:\s*(?P<base>\w+)\s*\{(?P<body>[^}]*)\}",
    re.DOTALL,
)
# One member: `Name = 12,` or `Name,`
MEMBER_RE = re.compile(r"^\s*(?P<name>[A-Za-z_]\w*)\s*(?:=\s*(?P<value>-?\w+))?\s*,?\s*$")

# The same, with the `: type` optional. The enums below are declared without
# one, and a second pattern keeps that from loosening what the protocol classes
# match.
BARE_ENUM_RE = re.compile(
    r"public\s+enum\s+(?P<name>\w+)\s*(?::\s*(?P<base>\w+)\s*)?\{(?P<body>[^}]*)\}",
    re.DOTALL,
)

# Enums that are not part of any one protocol but travel inside messages of
# several, each in a file of its own.
#
# The width is ours to state, because the client's declaration does not carry
# it: every one of these is a plain `enum` (so int in C#) and what actually goes
# on the wire is decided by how the client reads it back — `ReadByte` for most,
# `ReadUInt16` for the card view. Get it wrong and every field after it shifts.
SHARED_ENUMS = {
    "Faction": "byte",           # PlayerProtocol: (Faction)br.ReadByte()
    "CardView": "ushort",        # CatalogueProtocol: (CardView)br.ReadUInt16()
    "GameLocation": "byte",      # SceneProtocol: (GameLocation)br.ReadByte()
    "TransSceneType": "byte",    # SceneProtocol, alongside the location
    "LoginError": "byte",        # LoginProtocol: (LoginError)br.ReadByte()
    "ConnectType": "byte",       # LoginProtocol: written as w.Write((byte)...)
    "AvatarItem": "byte",        # AvatarItems: items[(AvatarItem)r.ReadByte()]
}


def parse_enum_members(body: str) -> dict[str, int]:
    """Resolves C# enum values, including implicit auto-increment."""
    members: dict[str, int] = {}
    nxt = 0
    for line in body.splitlines():
        line = line.split("//")[0]
        if not line.strip():
            continue
        m = MEMBER_RE.match(line)
        if not m:
            continue
        raw = m.group("value")
        if raw is None:
            value = nxt
        elif raw in members:          # alias: `A = B`
            value = members[raw]
        else:
            try:
                value = int(raw, 0)   # supports 0x...
            except ValueError:
                continue
        members[m.group("name")] = value
        nxt = value + 1
    return members


def enums_in(path: Path, pattern: re.Pattern[str] = ENUM_RE) -> dict[str, dict]:
    """Every enum in a file. The pattern decides which shapes count."""
    src = path.read_text(encoding="utf-8", errors="replace")
    return {
        m.group("name"): {
            "base": m.group("base"),
            "members": parse_enum_members(m.group("body")),
        }
        for m in pattern.finditer(src)
    }


def main() -> int:
    src_dir = Path(sys.argv[1] if len(sys.argv) > 1 else "client-ref/decompiled")
    out_path = Path(sys.argv[2] if len(sys.argv) > 2 else "spec/protocol.json")

    # 1. Routing enum: ProtocolID (which protocol handles each message).
    base = enums_in(src_dir / "BgoProtocol.cs")
    if "ProtocolID" not in base:
        print("ERROR: ProtocolID not found in BgoProtocol.cs", file=sys.stderr)
        return 1
    protocol_ids = base["ProtocolID"]["members"]

    # 2. For each *Protocol.cs class, its Request / Reply enums.
    protocols: dict[str, dict] = {}
    for cs in sorted(src_dir.glob("*Protocol.cs")):
        name = cs.stem                       # e.g. "LoginProtocol"
        short = name[: -len("Protocol")]     # e.g. "Login"
        if short not in protocol_ids:
            continue                         # skip helpers (parsers, views)
        enums = enums_in(cs)
        protocols[short] = {
            "id": protocol_ids[short],
            "source": cs.name,
            # client -> server
            "requests": enums.get("Request", {}).get("members", {}),
            # server -> client
            "replies": enums.get("Reply", {}).get("members", {}),
            # the protocol's auxiliary enums (error codes, flags...)
            "other_enums": {
                k: v["members"]
                for k, v in enums.items()
                if k not in ("Request", "Reply")
            },
        }

    # 3. Protocols declared in ProtocolID but with no class of their own.
    missing = sorted(set(protocol_ids) - set(protocols))

    # 4. Enums shared across protocols, each in its own file.
    shared: dict[str, dict] = {}
    for name, wire_type in sorted(SHARED_ENUMS.items()):
        path = src_dir / f"{name}.cs"
        if not path.exists():
            print(f"ERROR: {path} not found", file=sys.stderr)
            return 1

        found = enums_in(path, BARE_ENUM_RE)
        if name not in found:
            print(f"ERROR: enum {name} not found in {path.name}", file=sys.stderr)
            return 1

        shared[name] = {
            "source": path.name,
            "wire_type": wire_type,
            "members": found[name]["members"],
        }

    spec = {
        "_note": (
            "Interface specification derived from the BSGO client for "
            "interoperability. Message ids and names only; no code."
        ),
        "protocol_ids": protocol_ids,
        "protocols": protocols,
        "protocols_without_client_class": missing,
        "shared_enums": shared,
    }

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(spec, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    total_req = sum(len(p["requests"]) for p in protocols.values())
    total_rep = sum(len(p["replies"]) for p in protocols.values())
    print(f"wrote {out_path}")
    print(f"  protocols in ProtocolID : {len(protocol_ids)}")
    print(f"  with a client class     : {len(protocols)}")
    print(f"  requests (C->S)         : {total_req}")
    print(f"  replies  (S->C)         : {total_rep}")
    print(f"  shared enums            : {len(shared)}"
          f" ({sum(len(e['members']) for e in shared.values())} members)")
    if missing:
        print(f"  without a client class  : {', '.join(missing)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

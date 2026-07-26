#!/usr/bin/env python3
"""Builds the avatar catalogue from the client's assets.

The original server sent the list of pieces available to create a character.
That data does not ship with the client, but the *names* do: they are those of
the meshes, materials and textures inside the assetbundles.

Two rules the client imposes that are not obvious:

1. Keys are looked up by exact name; if one is missing, the client throws
   KeyNotFoundException while reading the card and draws nothing:

       cylon : pieces head/arms/body/legs, colours head_/arms_/body_/legs_
       human : pieces hair/head/suit/beard/glasses/helmet,
               colours hair_/beard_, textures faces_tex/hands_tex

2. The value is used verbatim as an asset name, so the extension matters:
   pieces carry none, materials carry ".mat" and textures their ".tga" or
   ".png". Without it the material never loads and the colour is not applied.

Usage (inside the container):
    python3 tools/generate_avatar_catalogue.py client-ref/assetbundles data/avatar-catalogue.json
"""
from __future__ import annotations

import json
import re
import sys
from collections import defaultdict
from pathlib import Path

import UnityPy


def material_key(item: str) -> str:
    """The client derives the colour key by appending "_" to the piece name."""
    return f"{item}_"


def texture_key(item: str) -> str:
    """...and the texture key by appending "_tex"."""
    return f"{item}_tex"


CYLON_ITEMS = ["head", "arms", "body", "legs"]

# Centurion meshes: centurion_<piece>_<version>. Auxiliary ones (_eye) are out.
CYLON_MESH_RE = re.compile(r"^centurion_(?P<item>[a-z]+)_(?P<version>v\d+)$")
# Materials: <mesh>_<colour>_<shade>
CYLON_MATERIAL_RE = re.compile(r"^(?P<mesh>centurion_[a-z]+_v\d+)_[a-z]+_\d+$")

# Human pieces: <sex>_<piece>_<n>, plus glasses/helmets/beards with their own names.
HUMAN_PIECE_RE = re.compile(r"^(?:male|female)_(?P<item>hair|head|suit)_\d+$")
GLASSES_RE = re.compile(r"^(?:female_)?glasses_\d+$")
HELMET_RE = re.compile(r"^helmet_\d+$")
BEARD_MESH_RE = re.compile(r"^volume_beard_\d+_\d+$")

# Face and hand textures, numbered.
FACE_RE = re.compile(r"^(?P<sex>male|female)_face_\d+$")
HANDS_RE = re.compile(r"^(?P<sex>male|female)_hands\d+$")

# Materials grouped by the mesh they belong to: <mesh> or <mesh>_<n>.
HAIR_MATERIAL_RE = re.compile(r"^(?P<mesh>(?:male|female)_hair_\d+)(?:_\d+)?$")
BEARD_MATERIAL_RE = re.compile(r"^(?P<mesh>volume_beard_\d+_\d+)(?:_\d+)?$")

# The "none" options, which the client itself uses as defaults.
EMPTY_OPTIONS = {
    "glasses": "glasses_empty",
    "helmet": "helmet_empty",
    "beard": "volume_beard_empty",
}


def natural_key(name: str) -> tuple:
    """Sorts face_2 before face_10, instead of the other way round."""
    return tuple(int(part) if part.isdigit() else part for part in re.split(r"(\d+)", name))


def names_of(bundle: Path, type_name: str) -> set[str]:
    """Names of the objects of one type inside an assetbundle."""
    if not bundle.exists():
        print(f"  warning: bundle {bundle.name} is missing", file=sys.stderr)
        return set()

    env = UnityPy.load(str(bundle))
    found = set()
    for obj in env.objects:
        if obj.type.name != type_name:
            continue
        try:
            name = obj.read().m_Name
        except Exception:
            continue
        if name:
            found.add(name)
    return found


def group_materials(names: set[str], pattern: re.Pattern) -> dict[str, list[str]]:
    """Groups materials by the mesh they apply to, appending ".mat"."""
    grouped: dict[str, list[str]] = defaultdict(list)
    for name in sorted(names, key=natural_key):
        m = pattern.match(name)
        if m:
            grouped[m.group("mesh")].append(f"{name}.mat")
    return dict(grouped)


def build_cylon(bundles: Path) -> dict:
    items: dict[str, list[str]] = {item: [] for item in CYLON_ITEMS}
    for name in sorted(names_of(bundles / "avatar_centurion", "Mesh")):
        m = CYLON_MESH_RE.match(name)
        if m and m.group("item") in items:
            items[m.group("item")].append(name)

    materials: dict[str, dict[str, list[str]]] = {material_key(i): {} for i in CYLON_ITEMS}
    for name in sorted(names_of(bundles / "avatar_centurion_materials", "Material")):
        m = CYLON_MATERIAL_RE.match(name)
        if not m:
            continue
        mesh = m.group("mesh")
        mesh_match = CYLON_MESH_RE.match(mesh)
        if not mesh_match:
            continue
        key = material_key(mesh_match.group("item"))
        if key in materials:
            materials[key].setdefault(mesh, []).append(f"{name}.mat")

    return {
        "sex": "centurion",
        "race": "cylon",
        "items": items,
        "materials": materials,
        "textures": {},          # the client expects no textures for cylon
    }


def build_human(bundles: Path, sex: str) -> dict:
    body_meshes = names_of(bundles / f"avatar_{sex}", "Mesh")

    items: dict[str, list[str]] = {"hair": [], "head": [], "suit": [], "beard": [], "glasses": [], "helmet": []}
    for name in sorted(body_meshes, key=natural_key):
        piece = HUMAN_PIECE_RE.match(name)
        if piece and name.startswith(sex):
            items[piece.group("item")].append(name)
        elif GLASSES_RE.match(name):
            items["glasses"].append(name)
        elif HELMET_RE.match(name):
            items["helmet"].append(name)
        elif BEARD_MESH_RE.match(name):
            items["beard"].append(name)

    # The "none" option goes first: it is the one the client defaults to.
    for item, empty in EMPTY_OPTIONS.items():
        items[item].insert(0, empty)

    hair_bundle = bundles / ("avatar_male_hair_mateials" if sex == "male" else "avatar_female_hair_materials")
    materials = {
        material_key("hair"): group_materials(names_of(hair_bundle, "Material"), HAIR_MATERIAL_RE),
        material_key("beard"): group_materials(
            names_of(bundles / "avatar_male_beard_materials", "Material"), BEARD_MATERIAL_RE),
    }
    # With no beard there is no beard colour, but the key must exist anyway.
    materials[material_key("beard")].setdefault(EMPTY_OPTIONS["beard"], [""])

    faces = sorted((n for n in names_of(bundles / f"avatar_{sex}_faces", "Texture2D") if FACE_RE.match(n)),
                   key=natural_key)
    hands = sorted((n for n in names_of(bundles / f"avatar_{sex}_hands", "Texture2D") if HANDS_RE.match(n)),
                   key=natural_key)

    return {
        "sex": sex,
        "race": "human",
        "items": items,
        "materials": materials,
        "textures": {
            texture_key("faces"): [f"{n}.tga" for n in faces],
            texture_key("hands"): [f"{n}.png" for n in hands],
        },
    }


def main() -> int:
    bundles = Path(sys.argv[1] if len(sys.argv) > 1 else "client-ref/assetbundles")
    out_path = Path(sys.argv[2] if len(sys.argv) > 2 else "data/avatar-catalogue.json")

    catalogue = {
        "_note": (
            "Avatar catalogue generated from the client's assetbundles. "
            "The keys are the ones the client looks up by exact name, and the "
            "values carry the extension it expects when loading the asset. "
            "Regenerate with tools/generate_avatar_catalogue.py."
        ),
        "avatars": [
            build_human(bundles, "male"),
            build_human(bundles, "female"),
            build_cylon(bundles),
        ],
    }

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(catalogue, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"wrote {out_path}")
    for avatar in catalogue["avatars"]:
        pieces = sum(len(v) for v in avatar["items"].values())
        colours = sum(len(v) for slot in avatar["materials"].values() for v in slot.values())
        textures = sum(len(v) for v in avatar["textures"].values())
        print(f"  {avatar['race']}/{avatar['sex']}: "
              f"{pieces} pieces, {colours} colours, {textures} textures")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env bash
# Launches the BSGO client against the local server.
#
# Uses the Wine prefix and runner you already have installed. The arguments the
# client expects used to come from Bigpoint's website; the values here are
# development ones, which the server accepts while AllowAnyCredentials is on.
#
# Two client quirks, both found by reading it:
#
#   1. The port is FIXED (27050). +gameServer only sets the IP; the port is
#      baked into the code and cannot be changed on the command line.
#
#   2. The client concatenates +gameServer, +cdn, +language, +session and
#      +version into a single string and splits it on spaces. If the +cdn path
#      contains spaces (and "Program Files (x86)" does) the parse goes out of
#      step, the client treats the arguments as invalid and shows "Please start
#      the game using the Launcher!". Hence C:\bsgo, a path without spaces.
set -euo pipefail

PREFIX="${WINEPREFIX:-/home/javi/Games/battlestar-galactica-online}"
WINE_BIN="${WINE:-/home/javi/.steam/steam/compatibilitytools.d/GE-Proton11-1/files/bin/wine}"
CLIENT_DIR="$PREFIX/drive_c/Program Files (x86)/BSGOFUN/client/live"

# Space-free link inside drive_c (see note 2 above). Created if missing.
CLIENT_LINK="$PREFIX/drive_c/bsgo"
[[ -e "$CLIENT_LINK" ]] || ln -sfn "$CLIENT_DIR" "$CLIENT_LINK"

SERVER="${1:-127.0.0.1}"
PLAYER_ID="${PLAYER_ID:-5085935}"
PLAYER_NAME="${PLAYER_NAME:-Starbuck}"

for path in "$WINE_BIN" "$CLIENT_DIR/bsgo.exe"; do
    [[ -e "$path" ]] || { echo "Not found: $path" >&2; exit 1; }
done

echo "Client : $CLIENT_DIR"
echo "Server : $SERVER:27050  (port fixed in the client)"
echo "Player : $PLAYER_NAME (id $PLAYER_ID)"
echo

cd "$CLIENT_DIR"
WINEPREFIX="$PREFIX" WINEDEBUG="${WINEDEBUG:--all}" "$WINE_BIN" bsgo.exe \
    +projectID 547 \
    +userID "$PLAYER_ID" \
    +sessionID 00000000000000000000000000000000 \
    +trackingID 00000000000000000000000000000000 \
    +gameServer "$SERVER" \
    +cdn 'C:\bsgo\' \
    +language en \
    +session 0000000000000000000000000000000000000000000000000000000000000000 \
    +version 3b27980a3b7dd77e597872106ca98000

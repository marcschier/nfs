#!/usr/bin/env bash
# Cross-implementation interop check: runs the Nfs local-disk server on Linux and drives it with a
# from-scratch Python NFSv3/MOUNT client that shares no code with the C# library. Verifies the
# server speaks ONC/RPC + NFSv3 + MOUNTv3 correctly on the wire against an independent implementation.
#
# Requirements: the .NET 10 runtime and python3. Run from any Linux host (including WSL):
#   ./eng/interop/run-interop.sh
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
WORK_DIR="$REPO/eng/interop/.run-interop-work"
EXPORT_DIR="$WORK_DIR/export"
NFS_PORT="${NFS_PORT:-12049}"
MOUNT_PORT="${MOUNT_PORT:-12050}"
DLL="$REPO/samples/Nfs.LocalDiskServer/bin/Release/net10.0/Nfs.LocalDiskServer.dll"
SERVER_PID=""

cleanup() {
    set +e
    if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
        kill -INT "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || kill "$SERVER_PID" 2>/dev/null || true
    fi
    rm -rf "$WORK_DIR"
}
trap cleanup EXIT

rm -rf "$WORK_DIR"
mkdir -p "$EXPORT_DIR"
printf 'Hello from the Nfs local-disk server!\n' > "$EXPORT_DIR/readme.txt"

if [ ! -f "$DLL" ]; then
    echo "Building the local-disk server sample..."
    dotnet build "$REPO/samples/Nfs.LocalDiskServer/Nfs.LocalDiskServer.csproj" -c Release >/dev/null
fi

echo "Starting the NFS server on Linux (nfs=$NFS_PORT mount=$MOUNT_PORT)..."
dotnet "$DLL" "$EXPORT_DIR" --serve --nfs-port "$NFS_PORT" --mount-port "$MOUNT_PORT" > "$WORK_DIR/server.log" 2>&1 &
SERVER_PID=$!
sleep 6

if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "The server failed to start:"
    cat "$WORK_DIR/server.log"
    exit 1
fi

python3 "$HERE/nfs3_interop_client.py" 127.0.0.1 "$MOUNT_PORT" "$NFS_PORT" /export readme.txt \
    "Hello from the Nfs local-disk server"

echo "Interop check passed."

#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK_DIR="$ROOT_DIR/eng/interop/.kernel-mount-work"
EXPORT_DIR="$WORK_DIR/export"
MOUNT_DIR="${MOUNT_DIR:-/mnt/nfstest}"
SECOND_MOUNT_DIR="${SECOND_MOUNT_DIR:-${MOUNT_DIR}-second}"
NFS_PORT="${NFS_PORT:-20490}"
MOUNT_PORT="${MOUNT_PORT:-20491}"
NFS_VERSION="${NFS_VERSION:-3}"
REGISTER="${REGISTER:-1}"
REQUIRE_RPCBIND="${REQUIRE_RPCBIND:-0}"
PROVOKE_DELEGATION_RECALL="${PROVOKE_DELEGATION_RECALL:-0}"
EXPORT_NAME="/export"
SERVER_LOG="$WORK_DIR/server.log"
SERVER_PID=""

cleanup() {
  set +e
  if mountpoint -q "$MOUNT_DIR"; then
    sudo umount "$MOUNT_DIR"
  fi
  if mountpoint -q "$SECOND_MOUNT_DIR"; then
    sudo umount "$SECOND_MOUNT_DIR"
  fi
  sudo rmdir "$MOUNT_DIR" 2>/dev/null || true
  sudo rmdir "$SECOND_MOUNT_DIR" 2>/dev/null || true
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    # Ask for a graceful shutdown, then bound the wait: poll for up to ~10s and
    # force-kill if the server ignores SIGINT, so teardown can never hang the job.
    kill -INT "$SERVER_PID" 2>/dev/null || true
    for _ in {1..50}; do
      kill -0 "$SERVER_PID" 2>/dev/null || break
      sleep 0.2
    done
    if kill -0 "$SERVER_PID" 2>/dev/null; then
      kill -KILL "$SERVER_PID" 2>/dev/null || true
    fi
    # $SERVER_PID is a direct child, so wait now reaps it promptly.
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  rm -rf "$WORK_DIR"
}
trap cleanup EXIT

usage() {
  cat <<USAGE
Usage: NFS_PORT=20490 MOUNT_PORT=20491 NFS_VERSION=3 $0

Environment:
  NFS_PORT         TCP port for the NFS service (default: 20490)
  MOUNT_PORT       TCP port for the MOUNT service (default: 20491)
  NFS_VERSION      Kernel client version to mount (default: 3)
  MOUNT_DIR        Local mount point (default: /mnt/nfstest)
  SECOND_MOUNT_DIR Second local mount point for v4 delegation recall probing
  REGISTER         Pass --register to the server sample when 1 (default: 1)
  REQUIRE_RPCBIND  Require rpcinfo/showmount verification when 1 (default: 0)
  PROVOKE_DELEGATION_RECALL  Best-effort v4.1 two-mount recall probe when 1 (default: 0)
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

rm -rf "$WORK_DIR"
mkdir -p "$EXPORT_DIR/docs"
sudo mkdir -p "$MOUNT_DIR"
printf 'Hello from the Nfs kernel interop export!\n' > "$EXPORT_DIR/readme.txt"

DEFAULT_SERVER_DLL="$ROOT_DIR/samples/Nfs.LocalDiskServer/bin/Release/net10.0/Nfs.LocalDiskServer.dll"
SERVER_DLL="${SERVER_DLL:-$DEFAULT_SERVER_DLL}"
if [[ ! -f "$SERVER_DLL" ]]; then
  dotnet build "$ROOT_DIR/samples/Nfs.LocalDiskServer/Nfs.LocalDiskServer.csproj" -c Release >/dev/null
  SERVER_DLL="$DEFAULT_SERVER_DLL"
fi

server_args=("$EXPORT_DIR" --serve --nfs-port "$NFS_PORT" --mount-port "$MOUNT_PORT")
if [[ "$REGISTER" == "1" ]]; then
  server_args+=(--register)
fi

dotnet "$SERVER_DLL" "${server_args[@]}" > "$SERVER_LOG" 2>&1 &
SERVER_PID="$!"

for _ in {1..50}; do
  if (echo > "/dev/tcp/127.0.0.1/$NFS_PORT") >/dev/null 2>&1 &&
     (echo > "/dev/tcp/127.0.0.1/$MOUNT_PORT") >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    cat "$SERVER_LOG"
    exit 1
  fi
  sleep 0.2
done

if ! (echo > "/dev/tcp/127.0.0.1/$NFS_PORT") >/dev/null 2>&1; then
  echo "NFS server did not become ready on port $NFS_PORT."
  cat "$SERVER_LOG"
  exit 1
fi

verify_rpcbind() {
  if ! command -v rpcinfo >/dev/null 2>&1 || ! command -v showmount >/dev/null 2>&1; then
    if [[ "$REQUIRE_RPCBIND" == "1" ]]; then
      echo "rpcinfo and showmount are required but not installed."
      exit 1
    fi
    return 0
  fi

  local rpcinfo_out="$WORK_DIR/rpcinfo.txt"
  local showmount_out="$WORK_DIR/showmount.txt"
  local found=0
  for _ in {1..50}; do
    rpcinfo -p 127.0.0.1 > "$rpcinfo_out" 2>&1 || true
    if grep -Eq "^[[:space:]]*100003[[:space:]]+3[[:space:]]+tcp[[:space:]]+$NFS_PORT([[:space:]]|$)" "$rpcinfo_out" &&
       grep -Eq "^[[:space:]]*100005[[:space:]]+3[[:space:]]+tcp[[:space:]]+$MOUNT_PORT([[:space:]]|$)" "$rpcinfo_out"; then
      found=1
      break
    fi
    sleep 0.2
  done

  cat "$rpcinfo_out"
  if [[ "$found" != "1" ]]; then
    if [[ "$REQUIRE_RPCBIND" == "1" ]]; then
      echo "rpcbind did not report the expected NFS/MOUNT registrations."
      cat "$SERVER_LOG"
      exit 1
    fi
    echo "rpcbind registrations were not observed; continuing because REQUIRE_RPCBIND=0."
    return 0
  fi

  showmount -e 127.0.0.1 > "$showmount_out" 2>&1 || {
    cat "$showmount_out"
    if [[ "$REQUIRE_RPCBIND" == "1" ]]; then
      exit 1
    fi
    return 0
  }
  cat "$showmount_out"
  if ! grep -Eq "(^|[[:space:]])$EXPORT_NAME([[:space:]]|$)" "$showmount_out"; then
    echo "showmount did not list $EXPORT_NAME."
    if [[ "$REQUIRE_RPCBIND" == "1" ]]; then
      exit 1
    fi
  fi
}

if [[ "$REGISTER" == "1" ]]; then
  verify_rpcbind
fi

mount_options="vers=$NFS_VERSION,proto=tcp,port=$NFS_PORT,nolock"
if [[ "$NFS_VERSION" == "3" ]]; then
  mount_options="$mount_options,mountport=$MOUNT_PORT"
fi

sudo mount -t nfs -o "$mount_options" "127.0.0.1:$EXPORT_NAME" "$MOUNT_DIR"

sudo ls -la "$MOUNT_DIR"
readme="$(sudo cat "$MOUNT_DIR/readme.txt")"
if [[ "$readme" != "Hello from the Nfs kernel interop export!" ]]; then
  echo "Unexpected readme.txt content: $readme"
  exit 1
fi

write_content="kernel-write-ok-$NFS_VERSION"
printf '%s' "$write_content" | sudo tee "$MOUNT_DIR/kernel-write.txt" >/dev/null
actual="$(sudo cat "$MOUNT_DIR/kernel-write.txt")"
if [[ "$actual" != "$write_content" ]]; then
  echo "Unexpected kernel-write.txt content: $actual"
  exit 1
fi

sudo mv "$MOUNT_DIR/kernel-write.txt" "$MOUNT_DIR/kernel-renamed.txt"
if [[ "$(sudo cat "$MOUNT_DIR/kernel-renamed.txt")" != "$write_content" ]]; then
  echo "Unexpected kernel-renamed.txt content after rename."
  exit 1
fi

sudo rm "$MOUNT_DIR/kernel-renamed.txt"
if sudo test -e "$MOUNT_DIR/kernel-renamed.txt"; then
  echo "kernel-renamed.txt still exists after delete."
  exit 1
fi

provoke_delegation_recall() {
  if [[ "$PROVOKE_DELEGATION_RECALL" != "1" || "$NFS_VERSION" != 4* ]]; then
    return 0
  fi

  if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 not available; skipping best-effort delegation recall probe."
    return 0
  fi

  sudo mkdir -p "$SECOND_MOUNT_DIR"
  sudo mount -t nfs -o "$mount_options" "127.0.0.1:$EXPORT_NAME" "$SECOND_MOUNT_DIR"
  printf 'delegation-recall-initial' | sudo tee "$MOUNT_DIR/delegation-recall.txt" >/dev/null

  sudo python3 - "$MOUNT_DIR/delegation-recall.txt" "$SECOND_MOUNT_DIR/delegation-recall.txt" <<'PY'
import sys
import threading
import time

read_path = sys.argv[1]
write_path = sys.argv[2]

def hold_read_open():
    with open(read_path, "rb") as handle:
        handle.read(1)
        time.sleep(1.0)

thread = threading.Thread(target=hold_read_open)
thread.start()
time.sleep(0.2)
with open(write_path, "wb") as handle:
    handle.write(b"delegation-recall-write")
thread.join()
PY

  recalled="$(sudo cat "$MOUNT_DIR/delegation-recall.txt")"
  if [[ "$recalled" != "delegation-recall-write" ]]; then
    echo "Delegation recall probe write was not visible through the first mount."
    exit 1
  fi

  sudo umount "$SECOND_MOUNT_DIR"
  sudo rmdir "$SECOND_MOUNT_DIR" 2>/dev/null || true
  echo "Best-effort NFSv$NFS_VERSION delegation recall probe completed."
}

provoke_delegation_recall

sudo ls -la "$MOUNT_DIR"
sudo umount "$MOUNT_DIR"

echo "KERNEL NFSv$NFS_VERSION INTEROP OK"

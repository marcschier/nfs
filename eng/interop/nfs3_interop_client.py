#!/usr/bin/env python3
"""A from-scratch ONC/RPC + NFSv3 + MOUNTv3 client used to verify, end to end and against an
independent implementation, that the Nfs server speaks the protocol correctly on the wire.

It shares no code with the C# library: it re-implements record marking, the RPC call/reply
headers (RFC 5531), and the handful of XDR shapes it needs (RFC 1813). It mounts an export,
gets the root attributes, looks up a file, and reads it, asserting the expected results.

Usage: nfs3_interop_client.py <host> <mount_port> <nfs_port> <export> <file> <expected_substring>
"""
import socket
import struct
import sys

MOUNT_PROGRAM, MOUNT_VERSION, MOUNTPROC3_MNT = 100005, 3, 1
NFS_PROGRAM, NFS_VERSION = 100003, 3
NFSPROC3_GETATTR, NFSPROC3_LOOKUP, NFSPROC3_READ = 1, 3, 6


class RpcClient:
    def __init__(self, host, port):
        self._sock = socket.create_connection((host, port), timeout=15)
        self._xid = 1000

    def call(self, program, version, procedure, body):
        self._xid += 1
        header = struct.pack(">IIIIII", self._xid, 0, 2, program, version, procedure)
        auth_none = struct.pack(">II", 0, 0)  # AUTH_NONE credential and verifier
        message = header + auth_none + auth_none + body
        record = struct.pack(">I", 0x80000000 | len(message)) + message
        self._sock.sendall(record)
        return self._receive_reply()

    def close(self):
        self._sock.close()

    def _receive_exact(self, count):
        data = b""
        while len(data) < count:
            chunk = self._sock.recv(count - len(data))
            if not chunk:
                raise EOFError("the connection was closed")
            data += chunk
        return data

    def _receive_record(self):
        out = b""
        while True:
            (marker,) = struct.unpack(">I", self._receive_exact(4))
            out += self._receive_exact(marker & 0x7FFFFFFF)
            if marker & 0x80000000:
                return out

    def _receive_reply(self):
        message = self._receive_record()
        xid, message_type, reply_status = struct.unpack_from(">III", message, 0)
        if message_type != 1:
            raise ValueError("not an RPC reply")
        if reply_status != 0:
            raise ValueError(f"RPC message denied: {reply_status}")
        offset = 12
        _, verifier_length = struct.unpack_from(">II", message, offset)
        offset += 8 + align4(verifier_length)
        (accept_status,) = struct.unpack_from(">I", message, offset)
        if accept_status != 0:
            raise ValueError(f"RPC accept status {accept_status}")
        return message[offset + 4:]


def align4(n):
    return (n + 3) & ~3


def xdr_string(value):
    raw = value.encode()
    return struct.pack(">I", len(raw)) + raw + b"\x00" * (align4(len(raw)) - len(raw))


def xdr_opaque(raw):
    return struct.pack(">I", len(raw)) + raw + b"\x00" * (align4(len(raw)) - len(raw))


def read_opaque(message, offset):
    (length,) = struct.unpack_from(">I", message, offset)
    offset += 4
    raw = message[offset:offset + length]
    return raw, offset + align4(length)


def skip_post_op_attr(message, offset):
    (present,) = struct.unpack_from(">I", message, offset)
    offset += 4
    return offset + 84 if present else offset  # fattr3 is 21 four-byte words


def main():
    host, mount_port, nfs_port = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    export, filename, expected = sys.argv[4], sys.argv[5], sys.argv[6]

    mount = RpcClient(host, mount_port)
    result = mount.call(MOUNT_PROGRAM, MOUNT_VERSION, MOUNTPROC3_MNT, xdr_string(export))
    (mount_status,) = struct.unpack_from(">I", result, 0)
    if mount_status != 0:
        raise SystemExit(f"MNT {export} failed: status {mount_status}")
    root_handle, _ = read_opaque(result, 4)
    print(f"MOUNT {export} -> root handle ({len(root_handle)} bytes)")
    mount.close()

    nfs = RpcClient(host, nfs_port)

    result = nfs.call(NFS_PROGRAM, NFS_VERSION, NFSPROC3_GETATTR, xdr_opaque(root_handle))
    (status,) = struct.unpack_from(">I", result, 0)
    if status != 0:
        raise SystemExit(f"GETATTR failed: status {status}")
    file_type, mode = struct.unpack_from(">II", result, 4)[:2]
    print(f"GETATTR root -> type={file_type} (2=directory) mode={oct(mode)}")
    if file_type != 2:
        raise SystemExit("root is not a directory")

    lookup_body = xdr_opaque(root_handle) + xdr_string(filename)
    result = nfs.call(NFS_PROGRAM, NFS_VERSION, NFSPROC3_LOOKUP, lookup_body)
    (status,) = struct.unpack_from(">I", result, 0)
    if status != 0:
        raise SystemExit(f"LOOKUP {filename} failed: status {status}")
    file_handle, _ = read_opaque(result, 4)
    print(f"LOOKUP {filename} -> handle ({len(file_handle)} bytes)")

    read_body = xdr_opaque(file_handle) + struct.pack(">QI", 0, 4096)
    result = nfs.call(NFS_PROGRAM, NFS_VERSION, NFSPROC3_READ, read_body)
    (status,) = struct.unpack_from(">I", result, 0)
    if status != 0:
        raise SystemExit(f"READ failed: status {status}")
    offset = skip_post_op_attr(result, 4)
    count, eof = struct.unpack_from(">II", result, offset)
    offset += 8
    data, _ = read_opaque(result, offset)
    print(f"READ {filename} -> {count} bytes, eof={bool(eof)}: {data!r}")
    nfs.close()

    if expected.encode() not in data:
        raise SystemExit(f"unexpected file content; wanted to find {expected!r}")

    print("INTEROP OK: independent Python client round-tripped MOUNT + NFSv3 GETATTR/LOOKUP/READ")


if __name__ == "__main__":
    main()

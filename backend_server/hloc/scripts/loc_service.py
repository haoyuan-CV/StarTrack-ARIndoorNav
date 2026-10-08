#!/usr/bin/env python3
import os
import sys
import socket
import pickle
import struct
from pathlib import Path

# Ensure project root is importable no matter where this script is launched from.
SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from engine import HLocEngine
from localize import localize

SOCKET_PATH = str(PROJECT_ROOT.parent / "tmp" / "hloc_service.sock")


def recv_msg(conn):
    raw_len = b""
    while len(raw_len) < 4:
        chunk = conn.recv(4 - len(raw_len))
        if not chunk:
            return None
        raw_len += chunk

    msg_len = struct.unpack("!I", raw_len)[0]
    data = b""
    while len(data) < msg_len:
        packet = conn.recv(msg_len - len(data))
        if not packet:
            return None
        data += packet

    return pickle.loads(data)


def send_msg(conn, obj):
    data = pickle.dumps(obj, protocol=pickle.HIGHEST_PROTOCOL)
    conn.sendall(struct.pack("!I", len(data)) + data)


def main():
    os.makedirs(os.path.dirname(SOCKET_PATH), exist_ok=True)

    if os.path.exists(SOCKET_PATH):
        os.remove(SOCKET_PATH)

    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    server.bind(SOCKET_PATH)
    server.listen(1)

    print("Starting HLoc Engine...")
    engine = HLocEngine()
    print("Service ready ✅")

    try:
        while True:
            conn, _ = server.accept()
            try:
                req = recv_msg(conn)
                if req is None:
                    continue

                if req.get("cmd") == "exit":
                    print("Shutting down service...")
                    break

                prefix = req.get("prefix", [])
                image_name = req["image"]
                intrinsics = req.get("intrinsics")

                result = localize(engine, prefix, image_name, intrinsics)
                send_msg(conn, result)

            except Exception as e:
                try:
                    send_msg(conn, {"error": str(e)})
                except Exception:
                    pass
            finally:
                conn.close()
    finally:
        server.close()
        if os.path.exists(SOCKET_PATH):
            os.remove(SOCKET_PATH)


if __name__ == "__main__":
    main()

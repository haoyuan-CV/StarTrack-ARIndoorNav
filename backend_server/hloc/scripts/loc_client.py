from __future__ import annotations

import os
import pickle
import socket
import struct
import sys
from pathlib import Path

# 让脚本可在未设置 PYTHONPATH 的情况下直接运行
PROJECT_ROOT = Path(__file__).resolve().parents[2]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from engine import HLocEngine
from localize import localize

SOCKET_PATH = str(PROJECT_ROOT.parent / "tmp" / "hloc_service.sock")


def _recv_exact(conn: socket.socket, size: int) -> bytes | None:
    data = b""
    while len(data) < size:
        packet = conn.recv(size - len(data))
        if not packet:
            return None
        data += packet
    return data


def recv_msg(conn):
    raw_len = _recv_exact(conn, 4)
    if not raw_len:
        return None

    msg_len = struct.unpack("!I", raw_len)[0]
    if msg_len <= 0:
        return None

    data = _recv_exact(conn, msg_len)
    if data is None:
        return None

    return pickle.loads(data)


def send_msg(conn, obj):
    data = pickle.dumps(obj, protocol=pickle.HIGHEST_PROTOCOL)
    conn.sendall(struct.pack("!I", len(data)) + data)


def main():
    # 删除旧 socket
    if os.path.exists(SOCKET_PATH):
        os.remove(SOCKET_PATH)

    # 启动 server
    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    server.bind(SOCKET_PATH)
    server.listen(5)

    print("Starting HLoc Engine...")
    engine = HLocEngine()
    print("Service ready ✅")

    try:
        while True:
            conn, _ = server.accept()

            try:
                req = recv_msg(conn)

                if req is None:
                    conn.close()
                    continue

                # 退出指令
                if req.get("cmd") == "exit":
                    print("Shutting down service...")
                    conn.close()
                    break

                # 正常请求
                prefix = req.get("prefix", [])
                image_name = req["image"]

                result = localize(engine, prefix, image_name)

                send_msg(conn, result)

            except Exception as e:
                send_msg(conn, {"error": str(e)})

            finally:
                conn.close()
    finally:
        server.close()

        # 清理 socket 文件
        if os.path.exists(SOCKET_PATH):
            os.remove(SOCKET_PATH)


if __name__ == "__main__":
    main()

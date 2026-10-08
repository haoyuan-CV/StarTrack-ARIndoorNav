import socket
import os
import pickle
import struct
from pathlib import Path

from engine import HLocEngine
from localize import localize

PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOCKET_PATH = str(PROJECT_ROOT.parent / "tmp" / "hloc_service.sock")


def recv_msg(conn):
    raw_len = conn.recv(4)
    if not raw_len:
        return None

    msg_len = struct.unpack("!I", raw_len)[0]

    data = b""
    while len(data) < msg_len:
        packet = conn.recv(msg_len - len(data))
        if not packet:
            return None
        data += packet

    return pickle.loads(data)


def send_msg(conn, obj):
    data = pickle.dumps(obj)
    conn.sendall(struct.pack("!I", len(data)) + data)


def main():
    # 删除旧 socket
    if os.path.exists(SOCKET_PATH):
        os.remove(SOCKET_PATH)

    # 启动 server
    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    server.bind(SOCKET_PATH)
    server.listen(1)

    print("Starting HLoc Engine...")
    engine = HLocEngine()
    print("Service ready ✅")

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

    server.close()

    # 清理 socket 文件
    if os.path.exists(SOCKET_PATH):
        os.remove(SOCKET_PATH)


if __name__ == "__main__":
    main()

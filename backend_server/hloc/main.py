import os
import json
import shutil
import socket
import pickle
import struct
from pathlib import Path

from fastapi import FastAPI, UploadFile, File, Form

from wifi_loc import WifiLocator
from prefix import locate_regions

app = FastAPI()

# =========================
# 路径配置
# =========================

BASE_DIR = Path(__file__).resolve().parent
HLOC_DIR = BASE_DIR / "hloc"

QUERY_DIR = HLOC_DIR / "datasets" / "query"
SOCKET_PATH = HLOC_DIR / "tmp" / "hloc_service.sock"

QUERY_DIR.mkdir(parents=True, exist_ok=True)
SOCKET_PATH.parent.mkdir(parents=True, exist_ok=True)

# =========================
# WiFi 初始化
# =========================

locator = WifiLocator(str(BASE_DIR / "radiomap.json"))

# =========================
# 串行锁
# =========================

IS_BUSY = False


# =========================
# socket 通信
# =========================

def send_msg(conn, obj):
    data = pickle.dumps(obj)
    conn.sendall(struct.pack("!I", len(data)) + data)


def recv_msg(conn):
    raw_len = conn.recv(4)
    if not raw_len:
        return None
    msg_len = struct.unpack("!I", raw_len)[0]

    data = b""
    while len(data) < msg_len:
        packet = conn.recv(msg_len - len(data))
        if not packet:
            break
        data += packet

    return pickle.loads(data)


def call_hloc(image_name, prefixes):
    conn = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    conn.settimeout(30)

    try:
        conn.connect(str(SOCKET_PATH))

        send_msg(conn, {
            "cmd": "localize",
            "image": image_name,
            "prefix": prefixes
        })

        result = recv_msg(conn)
        return result

    except Exception as e:
        return {"error": str(e)}

    finally:
        conn.close()


# =========================
# 主接口（保持你们协议）
# =========================

@app.post("/api/navigate")
async def handle_navigation(
    image: UploadFile = File(...),
    wifi_data_str: str = Form(...),
    destination_node: str = Form(...)
):
    global IS_BUSY

    if IS_BUSY:
        return {"status": "error", "message": "服务繁忙，请稍后再试"}

    IS_BUSY = True
    saved_path = None

    try:
        # ==========================================
        # Step 0：保存图片到 HLoc query目录
        # ==========================================

        image_name = image.filename
        saved_path = QUERY_DIR / image_name

        with open(saved_path, "wb") as buffer:
            shutil.copyfileobj(image.file, buffer)

        # ==========================================
        # Step 1：WiFi 定位
        # ==========================================

        try:
            wifi_payload = json.loads(wifi_data_str)
        except:
            return {"status": "error", "message": "WiFi 数据格式错误"}

        anyplace_result = locator.get_location(wifi_payload)

        if not anyplace_result:
            return {"status": "error", "message": "WiFi 定位失败"}

        wifi_x, wifi_y = anyplace_result

        print(f"📍 WiFi: ({wifi_x}, {wifi_y})")

        # ==========================================
        # Step 2：区域筛选（替换原 if-else）
        # ==========================================

        prefixes = locate_regions(wifi_x, wifi_y)

        if not prefixes:
            return {"status": "error", "message": "区域匹配失败"}

        print(f"🗺️ prefixes: {prefixes}")

        # ==========================================
        # Step 3：调用 HLoc（替换 get_pose）
        # ==========================================

        pose_result = call_hloc(image_name, prefixes)

        if not pose_result or "error" in pose_result:
            return {
                "status": "error",
                "message": pose_result.get("error", "视觉定位失败")
            }

        # ==========================================
        # Step 4：导航（保持接口，但你可以先关掉）
        # ==========================================

        # current_xyz = pose_result["C"]  # 如果你后面要用
        # path_coords = get_navigation_path(current_xyz, destination_node)

        path_coords = []  # 暂时占位

        # ==========================================
        # Step 5：返回（完全保持你们协议）
        # ==========================================

        return {
            "status": "success",
            "current_pose": pose_result,
            "navigation_path": path_coords
        }

    except Exception as e:
        return {"status": "error", "message": str(e)}

    finally:
        IS_BUSY = False

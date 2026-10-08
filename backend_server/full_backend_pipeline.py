import json
import os
import pickle
import shutil
import socket
import struct
import subprocess
import sys
import traceback
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Optional

from fastapi import FastAPI, File, Form, UploadFile
from scipy.spatial.transform import Rotation

PROJECT_ROOT = Path(__file__).resolve().parent
HLOC_DIR = PROJECT_ROOT / "hloc"
SCRIPTS_DIR = HLOC_DIR / "scripts"
QUERY_DIR = HLOC_DIR / "datasets" / "query"
TOPOLOGY_PATH = PROJECT_ROOT / "topology.json"
RADIOMAP_PATH = PROJECT_ROOT / "radiomap.json"
SOCKET_PATH = PROJECT_ROOT / "tmp" / "hloc_service.sock"

for path in (PROJECT_ROOT, HLOC_DIR, SCRIPTS_DIR):
    path_str = str(path)
    if path_str not in sys.path:
        sys.path.insert(0, path_str)

from navigator import get_navigation_path
from prefix import locate_regions
from semantic_destination import format_semantic_topk, resolve_destination
from wifi_loc import WifiLocator


hloc_process = None
locator = None

# Optional demo mode for recording stable presentation videos.
# Keep it disabled in the submitted source so navigation uses the real HLoc pose.
# The values below come from COLMAP image O_56.jpg. COLMAP stores cam_from_world
# as X_cam = R_cw * X_world + t_cw, so the camera center is C = -R_cw.T @ t_cw.
DEMO_FORCE_HLOC_POSE = False
DEMO_POSE_NAME = "O_56.jpg"
DEMO_C_WORLD = [6.174434687, -0.382331020, -1.115102258]
DEMO_Q_CAM2WORLD = [-0.026370495, -0.665865868, -0.017838996, 0.745391852]


@asynccontextmanager
async def lifespan(app: FastAPI):
    global hloc_process, locator

    print("[Startup] Starting persistent HLoc service...")
    QUERY_DIR.mkdir(parents=True, exist_ok=True)
    SOCKET_PATH.parent.mkdir(parents=True, exist_ok=True)

    locator = WifiLocator(str(RADIOMAP_PATH))

    env = os.environ.copy()
    env["PYTHONPATH"] = f"{PROJECT_ROOT}:{env.get('PYTHONPATH', '')}"
    loc_script = str(SCRIPTS_DIR / "loc_service.py")

    hloc_process = subprocess.Popen(
        [sys.executable, loc_script],
        env=env,
        cwd=str(HLOC_DIR),
    )

    yield

    print("[Shutdown] Stopping HLoc service...")
    if hloc_process:
        hloc_process.terminate()
        hloc_process.wait()


app = FastAPI(lifespan=lifespan)


def send_msg(conn: socket.socket, obj) -> None:
    data = pickle.dumps(obj, protocol=pickle.HIGHEST_PROTOCOL)
    conn.sendall(struct.pack("!I", len(data)) + data)


def recv_msg(conn: socket.socket):
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


def build_intrinsics(
    camera_model: Optional[str],
    image_width: Optional[int],
    image_height: Optional[int],
    fx: Optional[float],
    fy: Optional[float],
    cx: Optional[float],
    cy: Optional[float],
) -> Optional[dict]:
    values = (image_width, image_height, fx, fy, cx, cy)
    if any(value is None for value in values):
        return None

    if image_width <= 0 or image_height <= 0 or fx <= 0 or fy <= 0:
        return None

    return {
        "model": camera_model or "PINHOLE",
        "width": int(image_width),
        "height": int(image_height),
        "fx": float(fx),
        "fy": float(fy),
        "cx": float(cx),
        "cy": float(cy),
    }


def call_hloc(
    image_name: str,
    prefixes: list[str],
    intrinsics: Optional[dict] = None,
    timeout: int = 30,
):
    if not SOCKET_PATH.exists():
        return {"error": f"Socket does not exist: {SOCKET_PATH}", "stage": "socket"}

    conn = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    conn.settimeout(timeout)
    try:
        conn.connect(str(SOCKET_PATH))
        send_msg(
            conn,
            {
                "cmd": "localize",
                "image": image_name,
                "prefix": prefixes,
                "intrinsics": intrinsics,
            },
        )
        resp = recv_msg(conn)
        if resp is None:
            return {"error": "HLoc service returned no data", "stage": "socket_recv"}
        return resp
    except Exception as exc:
        return {
            "error": str(exc),
            "stage": "socket_call",
            "traceback": traceback.format_exc(),
        }
    finally:
        conn.close()


@app.post("/api/navigate")
async def handle_navigation(
    image: UploadFile = File(...),
    wifi_data_str: str = Form(...),
    destination_node: str = Form(...),
    camera_model: str = Form("PINHOLE"),
    image_width: Optional[int] = Form(None),
    image_height: Optional[int] = Form(None),
    fx: Optional[float] = Form(None),
    fy: Optional[float] = Form(None),
    cx: Optional[float] = Form(None),
    cy: Optional[float] = Form(None),
):
    try:
        image_path = QUERY_DIR / image.filename
        with open(image_path, "wb") as buffer:
            shutil.copyfileobj(image.file, buffer)

        wifi_payload = json.loads(wifi_data_str)
        if isinstance(wifi_payload, list):
            wifi_payload = {"APs": wifi_payload}

        wifi_pose = locator.get_location(wifi_payload)
        if not wifi_pose:
            return {"status": "error", "message": "WiFi localization failed"}

        wifi_x, wifi_y = wifi_pose
        prefixes = locate_regions(wifi_x, wifi_y)
        if not prefixes:
            return {"status": "error", "message": "WiFi region prefix matching failed"}

        intrinsics = build_intrinsics(
            camera_model,
            image_width,
            image_height,
            fx,
            fy,
            cx,
            cy,
        )
        if intrinsics:
            print(
                "[QueryIntrinsics] "
                f"model={intrinsics['model']} width={intrinsics['width']} height={intrinsics['height']} "
                f"fx={intrinsics['fx']:.3f} fy={intrinsics['fy']:.3f} "
                f"cx={intrinsics['cx']:.3f} cy={intrinsics['cy']:.3f}"
            )
        else:
            print("[QueryIntrinsics] Missing or invalid frontend intrinsics; HLoc will use fallback estimation.")

        pose_result = call_hloc(image.filename, prefixes, intrinsics)
        if "error" in pose_result:
            return {
                "status": "error",
                "message": f"Visual localization failed: {pose_result['error']}",
                "stage": pose_result.get("stage", "hloc"),
            }

        if "C" not in pose_result or "R" not in pose_result:
            return {"status": "error", "message": "HLoc result missing C or R"}

        C = pose_result["C"]
        if hasattr(C, "tolist"):
            C = C.tolist()

        R = pose_result["R"]
        if hasattr(R, "tolist"):
            R = R.tolist()

        if DEMO_FORCE_HLOC_POSE:
            real_c = [float(C[0]), float(C[1]), float(C[2])]
            print(
                "[DemoPose] "
                f"Overriding HLoc pose with {DEMO_POSE_NAME}. "
                f"real_C=({real_c[0]:.6f}, {real_c[1]:.6f}, {real_c[2]:.6f})"
            )
            C = list(DEMO_C_WORLD)
            qx, qy, qz, qw = DEMO_Q_CAM2WORLD
        else:
            rot = Rotation.from_matrix(R)
            qx, qy, qz, qw = rot.as_quat()

        unity_pose = {
            "world_x": float(C[0]),
            "world_y": float(C[1]),
            "world_z": float(C[2]),
            "qx": float(qx),
            "qy": float(qy),
            "qz": float(qz),
            "qw": float(qw),
        }

        semantic_result = resolve_destination(destination_node, str(TOPOLOGY_PATH))
        resolved_destination = semantic_result["destination_node"]
        semantic_topk = format_semantic_topk(semantic_result)
        print(
            "[SemanticDestination] "
            f"query={destination_node!r}, resolved={resolved_destination!r}, "
            f"score={semantic_result['score']:.4f}, method={semantic_result['method']}, "
            f"topk={semantic_topk}"
        )

        path_coords = get_navigation_path(C, resolved_destination, str(TOPOLOGY_PATH))

        intrinsics_msg = (
            f"intrinsics={intrinsics['model']} {intrinsics['width']}x{intrinsics['height']} "
            f"fx={intrinsics['fx']:.1f} fy={intrinsics['fy']:.1f}"
            if intrinsics
            else "intrinsics=fallback"
        )
        diagnostics = (
            f"Localization success. raw_query={destination_node}, "
            f"resolved_destination={resolved_destination}, "
            f"semantic_method={semantic_result['method']}, "
            f"semantic_top3={semantic_topk}, "
            f"WiFi=({wifi_x:.2f}, {wifi_y:.2f}), prefixes={prefixes}, "
            f"C_colmap=({float(C[0]):.3f}, {float(C[1]):.3f}, {float(C[2]):.3f}), "
            f"path_len={len(path_coords)}, {intrinsics_msg}"
        )
        if DEMO_FORCE_HLOC_POSE:
            diagnostics += f", demo_pose={DEMO_POSE_NAME}"
        print(f"[HLocPose] C_colmap=({float(C[0]):.6f}, {float(C[1]):.6f}, {float(C[2]):.6f})")
        print(f"[NavigateDebug] {diagnostics}")

        return {
            "status": "success",
            "message": f"Resolved destination: {resolved_destination}",
            "current_pose": unity_pose,
            "navigation_path": [{"x": p[0], "y": p[1], "z": p[2]} for p in path_coords],
            "diagnostics": diagnostics,
        }

    except Exception as exc:
        return {
            "status": "error",
            "message": f"Internal server error: {str(exc)}",
            "traceback": traceback.format_exc(),
        }

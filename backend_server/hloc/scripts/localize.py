from pathlib import Path
from copy import deepcopy
import numpy as np
import h5py
from hloc import extract_features, match_features, localize_sfm
from PIL import Image
import torch

# =========================
# ✅  PyTorch Hub 缓存
# =========================

custom_cache_dir = Path("./cache/hub")
custom_cache_dir.mkdir(parents=True, exist_ok=True)
torch.hub.set_dir(str(custom_cache_dir))

# =========================
# 路径
# =========================

library_images = Path("datasets/library")
query_images = Path("datasets/query")

outputs = Path("outputs/library")
loc_dir = outputs / "localization"

sfm_dir = outputs / "sfm/models"

features_ref = outputs / "features.h5"
netvlad_db_path = outputs / "sfm/netvlad_db.h5"

features_query = loc_dir / "features_query.h5"
netvlad_query = loc_dir / "netvlad_query.h5"

matches_query = loc_dir / "matches_query.h5"
pairs_query = loc_dir / "pairs-query.txt"
results = loc_dir / "results.txt"
query_list = loc_dir / "query_list.txt"

loc_dir.mkdir(parents=True, exist_ok=True)


# =========================
# quaternion → R
# =========================

def qvec2rotmat(qvec):
    qw, qx, qy, qz = qvec
    return np.array([
        [1 - 2*qy**2 - 2*qz**2, 2*qx*qy - 2*qz*qw, 2*qx*qz + 2*qy*qw],
        [2*qx*qy + 2*qz*qw, 1 - 2*qx**2 - 2*qz**2, 2*qy*qz - 2*qx*qw],
        [2*qx*qz - 2*qy*qw, 2*qy*qz + 2*qx*qw, 1 - 2*qx**2 - 2*qy**2]
    ])


# =========================
# 内参
# =========================

def get_query_intrinsics(image_path, intrinsics=None):
    img = Image.open(image_path)
    width, height = img.size

    if intrinsics:
        required = ("width", "height", "fx", "fy", "cx", "cy")
        if all(key in intrinsics and intrinsics[key] is not None for key in required):
            src_width = int(intrinsics["width"])
            src_height = int(intrinsics["height"])
            fx = float(intrinsics["fx"])
            fy = float(intrinsics["fy"])
            cx = float(intrinsics["cx"])
            cy = float(intrinsics["cy"])

            if src_width > 0 and src_height > 0 and fx > 0 and fy > 0:
                scale_x = width / src_width
                scale_y = height / src_height
                model = str(intrinsics.get("model", "PINHOLE")).upper()

                if model != "PINHOLE":
                    print(f"[QueryIntrinsics] Unsupported model={model}; using PINHOLE with provided fx/fy.")
                    model = "PINHOLE"

                params = [
                    fx * scale_x,
                    fy * scale_y,
                    cx * scale_x,
                    cy * scale_y,
                ]
                print(
                    "[QueryIntrinsics] "
                    f"model={model} width={width} height={height} "
                    f"fx={params[0]:.3f} fy={params[1]:.3f} "
                    f"cx={params[2]:.3f} cy={params[3]:.3f} "
                    f"source_width={src_width} source_height={src_height}"
                )
                return model, width, height, params

    model = "SIMPLE_RADIAL"
    focal = None

    try:
        exif = img._getexif()
        if exif:
            focal_35mm = exif.get(41989)
            if focal_35mm:
                focal = float(focal_35mm) * max(width, height) / 36.0
    except:
        pass

    if focal is None:
        fov = np.deg2rad(70.0)
        focal = width / (2 * np.tan(fov / 2))

    if focal < 0.5 * max(width, height) or focal > 3 * max(width, height):
        focal = 1.3 * max(width, height)

    cx = width / 2
    cy = height / 2

    print(
        "[QueryIntrinsics] "
        f"fallback model={model} width={width} height={height} "
        f"focal={focal:.3f} cx={cx:.3f} cy={cy:.3f}"
    )
    return model, width, height, [focal, cx, cy, 0.0]


# =========================
# NetVLAD
# =========================

def load_netvlad(path):
    descs = {}
    with h5py.File(path, "r") as f:
        for k in f.keys():
            descs[k] = f[k]["global_descriptor"][:]
    return descs


def netvlad_retrieval(qname, prefix=None, topk=10):
    db_desc = load_netvlad(netvlad_db_path)
    q_desc = load_netvlad(netvlad_query)[qname]

    # ✅ 统一prefix为list
    if prefix is not None:
        if isinstance(prefix, str):
            prefix = [prefix]

        db_desc = {
            k: v for k, v in db_desc.items()
            if any(k.startswith(p) for p in prefix)
        }

    scores = []
    for name, desc in db_desc.items():
        sim = np.dot(q_desc, desc)
        scores.append((name, sim))

    scores.sort(key=lambda x: -x[1])
    return [name for name, _ in scores[:topk]]


# =========================
# 核心函数
# =========================

def localize(engine, prefix, image_name, intrinsics=None):
    qname = image_name
    qpath = query_images / qname

    # =========================
    # Step 0：query内参
    # =========================

    model, width, height, params = get_query_intrinsics(qpath, intrinsics)

    with open(query_list, "w") as f:
        f.write(f"{qname} {model} {width} {height} " + " ".join(map(str, params)) + "\n")

    # =========================
    # Step 1：NetVLAD
    # =========================

    netvlad_conf = extract_features.confs["netvlad"]
    extract_features.main(
        netvlad_conf,
        query_images,
        image_list=[qname],
        feature_path=netvlad_query,
        overwrite=True,
        model=engine.netvlad,
    )

    # =========================
    # Step 2：Top-K retrieval
    # =========================

    topk_db = netvlad_retrieval(qname, prefix=prefix, topk=10)

    with open(pairs_query, "w") as f:
        for db_img in topk_db:
            f.write(f"{qname} {db_img}\n")

    # =========================
    # Step 3：SuperPoint
    # =========================

    feature_conf = deepcopy(extract_features.confs["superpoint_aachen"])
    matcher_conf = deepcopy(match_features.confs["superpoint+lightglue"])

    extract_features.main(
        feature_conf,
        query_images,
        image_list=[qname],
        feature_path=features_query,
        overwrite=True,
        model=engine.superpoint,
    )

    # =========================
    # Step 4：match
    # =========================

    match_features.main(
        matcher_conf,
        pairs_query,
        features=features_query,
        features_ref=features_ref,
        matches=matches_query,
        overwrite=True,
        model=engine.lightglue,
    )

    # =========================
    # Step 5：localize
    # =========================

    localize_sfm.main(
        sfm_dir,
        query_list,
        pairs_query,
        features_query,
        matches_query,
        results,
        covisibility_clustering=True,
    )

    # =========================
    # Step 6：位姿转换
    # =========================

    line = open(results).readlines()[0]
    parts = line.strip().split()

    q = np.array(list(map(float, parts[1:5])))
    t = np.array(list(map(float, parts[5:8])))

    R = qvec2rotmat(q)

    C = -R.T @ t
    Rcw = R.T

    return {
        "image": parts[0],
        "C": C,
        "R": Rcw,
        "query_intrinsics": {
            "model": model,
            "width": width,
            "height": height,
            "params": params,
        },
    }

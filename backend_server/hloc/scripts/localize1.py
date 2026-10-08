from pathlib import Path
from copy import deepcopy
import numpy as np

from hloc import extract_features, match_features, localize_sfm
from PIL import Image

# =========================
# 路径
# =========================

library_images = Path("datasets/library")
query_images = Path("datasets/query")

outputs = Path("outputs/library")
loc_dir = outputs / "localization"

sfm_dir = outputs / "sfm/models"

features_ref = outputs / "features.h5"   # ✅ database features

features_query = loc_dir / "features_query.h5"

matches_query = loc_dir / "matches_query.h5"
pairs_query = loc_dir / "pairs-query.txt"
results = loc_dir / "results.txt"

query_list = loc_dir / "query_list.txt"
db_list = loc_dir / "list.txt"

camera_pose_file = Path("camera_poses.txt")

loc_dir.mkdir(parents=True, exist_ok=True)


# =========================
# 🔥 quaternion → R
# =========================

def qvec2rotmat(qvec):
    qw, qx, qy, qz = qvec
    return np.array([
        [1 - 2*qy**2 - 2*qz**2, 2*qx*qy - 2*qz*qw, 2*qx*qz + 2*qy*qw],
        [2*qx*qy + 2*qz*qw, 1 - 2*qx**2 - 2*qz**2, 2*qy*qz - 2*qx*qw],
        [2*qx*qz - 2*qy*qw, 2*qy*qz + 2*qx*qw, 1 - 2*qx**2 - 2*qy**2]
    ])


# =========================
# 🔥 内参（手机优化）
# =========================

def get_query_intrinsics(image_path):
    img = Image.open(image_path)
    width, height = img.size

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

    return model, width, height, [focal, cx, cy, 0.0]


# =========================
# 🔥 读取 camera poses（转为C）
# =========================

def load_camera_poses(path):
    poses = {}

    with open(path, "r") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue

            parts = line.split()

            if len(parts) < 8:
                continue

            try:
                name = parts[0]

                q = np.array(list(map(float, parts[1:5])))
                t = np.array(list(map(float, parts[5:8])))

                R = qvec2rotmat(q)

                # ✅ 转成相机中心
                C = -R.T @ t

                poses[name] = C

            except:
                continue

    print(f"Loaded {len(poses)} poses")
    return poses


# =========================
# 🔥 最近邻
# =========================

def find_topk_nearest(query_C, db_poses, k=5):
    results = []

    for name, C in db_poses.items():
        dist = np.linalg.norm(query_C - C)
        results.append((name, dist))

    results.sort(key=lambda x: x[1])
    return results[:k]


# =========================
# Step 0
# =========================

queries = sorted([p.name for p in query_images.glob("*.jpg")])
if len(queries) != 1:
    raise ValueError("Query folder must contain exactly ONE image")

qname = queries[0]
qpath = query_images / qname

model, width, height, params = get_query_intrinsics(qpath)

with open(query_list, "w") as f:
    f.write(f"{qname} {model} {width} {height} " + " ".join(map(str, params)) + "\n")

with open(db_list, "r") as f:
    db_images = [l.strip() for l in f if l.strip()]


# =========================
# Step 1 pairs
# =========================

with open(pairs_query, "w") as f:
    for db_img in db_images:
        f.write(f"{qname} {db_img}\n")


# =========================
# Step 2 features（仅query）
# =========================

feature_conf = deepcopy(extract_features.confs["superpoint_aachen"])
matcher_conf = deepcopy(match_features.confs["superpoint+lightglue"])

extract_features.main(
    feature_conf,
    query_images,
    image_list=queries,
    feature_path=features_query,
    overwrite=True,
)


# =========================
# ❌ Step 3 merge（已彻底删除）
# =========================


# =========================
# Step 4 match（核心优化）
# =========================

match_features.main(
    matcher_conf,
    pairs_query,
    features=features_query,     # ✅ query features
    features_ref=features_ref,   # ✅ database features（关键）
    matches=matches_query,
    overwrite=True,
)


# =========================
# Step 5 localization（不变）
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
# Step 6 输出 + 最近邻（不变）
# =========================

print("\n===== Pose Result =====")

if not results.exists():
    print("❌ Localization failed")
else:
    lines = open(results).readlines()
    for line in lines:
        print(line.strip())

    if camera_pose_file.exists():
        db_poses = load_camera_poses(camera_pose_file)

        print("\n===== Top-5 Nearest =====")

        for line in lines:
            parts = line.strip().split()

            q = np.array(list(map(float, parts[1:5])))
            t = np.array(list(map(float, parts[5:8])))

            R = qvec2rotmat(q)
            query_C = -R.T @ t   # ✅ 统一成C

            top5 = find_topk_nearest(query_C, db_poses)

            print(f"\nQuery: {parts[0]}")
            for name, dist in top5:
                print(f"{name}  |  {dist:.4f}")

# =========================
# 🔥 Step 7 精验证（Top10中选最优位姿）
# =========================

def rotation_error(R1, R2):
    """计算两个旋转矩阵之间的角度误差（degree）"""
    R = R1 @ R2.T
    trace = np.clip((np.trace(R) - 1) / 2, -1.0, 1.0)
    angle = np.arccos(trace)
    return np.degrees(angle)


print("\n===== Pose Verification (Top10 → Best Pose) =====")

if results.exists() and camera_pose_file.exists():

    db_poses = load_camera_poses(camera_pose_file)

    # 👉 再读一遍，把 R 也存下来
    db_full = {}
    with open(camera_pose_file, "r") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue

            parts = line.split()
            if len(parts) < 8:
                continue

            try:
                name = parts[0]
                q = np.array(list(map(float, parts[1:5])))
                t = np.array(list(map(float, parts[5:8])))

                R = qvec2rotmat(q)
                C = -R.T @ t

                db_full[name] = (R, C)
            except:
                continue

    lines = open(results).readlines()

    for line in lines:
        parts = line.strip().split()

        q = np.array(list(map(float, parts[1:5])))
        t = np.array(list(map(float, parts[5:8])))

        Rq = qvec2rotmat(q)
        Cq = -Rq.T @ t

        # =========================
        # Step 7.1 找最近10个（空间）
        # =========================

        dists = []
        for name, (Rdb, Cdb) in db_full.items():
            dist = np.linalg.norm(Cq - Cdb)
            dists.append((name, dist))

        dists.sort(key=lambda x: x[1])
        top10 = dists[:10]

        # =========================
        # Step 7.2 在Top10中找最优位姿
        # =========================

        best_name = None
        best_score = 1e9
        best_rot = None
        best_trans = None

        for name, dist in top10:
            Rdb, Cdb = db_full[name]

            rot_err = rotation_error(Rq, Rdb)
            trans_err = np.linalg.norm(Cq - Cdb)

            # 👉 综合误差（你也可以自己调权重）
            score = rot_err + trans_err

            if score < best_score:
                best_score = score
                best_name = name
                best_rot = rot_err
                best_trans = trans_err

        print(f"\nQuery: {parts[0]}")
        print("Top10 candidates:", [n for n, _ in top10])
        print(f"✅ Best match: {best_name}")
        print(f"   Rotation error: {best_rot:.3f} deg")
        print(f"   Translation error: {best_trans:.4f}")
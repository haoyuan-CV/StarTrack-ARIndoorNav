from pathlib import Path
from copy import deepcopy

from hloc import extract_features, match_features, reconstruction


# =========================
# 路径
# =========================

images = Path("datasets/library")
outputs = Path("outputs/library")

features = outputs / "features.h5"
matches = outputs / "matches.h5"

# ✅ 使用你自己写的 pairs
sfm_pairs = outputs / "pairs.txt"

sfm_dir = outputs / "sfm"

outputs.mkdir(parents=True, exist_ok=True)


# =========================
# 配置
# =========================

feature_conf = deepcopy(extract_features.confs["superpoint_aachen"])
matcher_conf = deepcopy(match_features.confs["superglue"])


# ❗ 不 resize（保持4K）
feature_conf["preprocessing"]["resize_max"] = None


# ❗ 提高匹配质量（关键）
matcher_conf["model"]["match_threshold"] = 0.3
matcher_conf["model"]["sinkhorn_iterations"] = 50


# =========================
# 读取图片
# =========================

references = sorted([p.name for p in images.glob("*.jpg")])
print("Images:", len(references))

if len(references) == 0:
    raise ValueError("No images found!")


# =========================
# ⚠️ 检查 pairs 文件
# =========================

if not sfm_pairs.exists():
    raise ValueError(f"Pairs file not found: {sfm_pairs}")

print("Using custom pairs:", sfm_pairs)


# =========================
# Step 1: 局部特征
# =========================

print("Extracting local features...")

extract_features.main(
    feature_conf,
    images,
    image_list=references,
    feature_path=features,
    overwrite=True,
)


# =========================
# Step 2: 特征匹配
# =========================

print("Matching features (SuperGlue)...")

match_features.main(
    matcher_conf,
    sfm_pairs,
    features=features,
    matches=matches,
    overwrite=True,
)


# =========================
# Step 3: COLMAP 重建
# =========================

print("Running reconstruction...")

image_options = {
    "camera_model": "OPENCV",
    "camera_params": "2150,2150,1920,1080,0,0,0,0",
}

mapper_options = {
    # 1. 优化开关 (这些是 IncrementalPipelineOptions 的直接属性)
    "ba_refine_focal_length": True,
    "ba_refine_principal_point": True,
    "ba_refine_extra_params": True, 
}

model = reconstruction.main(
    sfm_dir,
    images,
    sfm_pairs,
    features,
    matches,
    image_list=references,
    image_options=image_options,
    mapper_options=mapper_options,
    camera_mode="SINGLE"
)

print(model)
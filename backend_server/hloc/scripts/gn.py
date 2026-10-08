from pathlib import Path
from hloc import extract_features

import torch
from pathlib import Path

# =========================
# 环境配置：自定义缓存路径
# =========================
# 将当前目录下的 cache/hub 设为 PyTorch 的 Hub 目录
custom_cache_dir = Path("./cache/hub")
custom_cache_dir.mkdir(parents=True, exist_ok=True)
torch.hub.set_dir(str(custom_cache_dir))

# =========================
# 路径（按你现有结构）
# =========================

library_images = Path("datasets/library")   # ← 从 sfm 目录回溯
sfm_dir = Path("outputs/library/sfm/")  # outputs/library/sfm/

camera_pose_file = Path("camera_poses.txt")

output_path = sfm_dir / "netvlad_db.h5"

# =========================
# 🔥 从 camera_poses.txt 读取图片名
# =========================

def load_image_list(pose_file):
    names = []
    with open(pose_file, "r") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split()
            if len(parts) < 1:
                continue
            names.append(parts[0])
    return names

db_images = load_image_list(camera_pose_file)

print(f"Loaded {len(db_images)} images from poses")

# =========================
# 🔥 NetVLAD 配置
# =========================

conf = extract_features.confs["netvlad"]

# =========================
# 🔥 提取
# =========================

extract_features.main(
    conf,
    library_images,
    image_list=db_images,   # ✅ 只提 SfM 内图片
    feature_path=output_path,
    overwrite=True,
)

print(f"\n✅ NetVLAD DB saved to: {output_path}")

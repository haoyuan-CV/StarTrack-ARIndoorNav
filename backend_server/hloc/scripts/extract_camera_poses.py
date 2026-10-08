from pathlib import Path
from hloc.utils.read_write_model import read_model
from scipy.spatial.transform import Rotation as R
import numpy as np

# =========================
# 路径
# =========================

sfm_dir = Path("outputs/library/sfm/models")
output_file = Path("camera_poses.txt")  # 根目录输出

# =========================
# 读取 COLMAP/SfM 模型
# =========================

cameras, images, points3D = read_model(sfm_dir)

print(f"Found {len(images)} cameras in the SfM model.")

# =========================
# 写入 txt 文件
# =========================

with open(output_file, "w") as f:
    f.write("# Format: image_name qw qx qy qz tx ty tz\n")
    for img_id, img in images.items():
        # COLMAP 中 R, t
        R_mat = img.qvec2rotmat()  # 3x3 rotation matrix
        tvec = img.tvec             # translation vector

        # 转换 rotation matrix -> quaternion (w, x, y, z)
        quat = R.from_matrix(R_mat).as_quat()  # returns [x, y, z, w]
        # HLoc/COLMAP 四元数顺序通常是 (w, x, y, z)
        quat_wxyz = [quat[3], quat[0], quat[1], quat[2]]

        # 输出格式
        line = f"{img.name} " + " ".join(f"{v:.6f}" for v in quat_wxyz + list(tvec))
        f.write(line + "\n")

print(f"Camera poses saved to {output_file}")
from __future__ import annotations

from pathlib import Path
import sys

import torch

# 让脚本可在未设置 PYTHONPATH 的情况下直接运行
PROJECT_ROOT = Path(__file__).resolve().parents[2]
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from hloc.extractors.netvlad import NetVLAD
from hloc.extractors.superpoint import SuperPoint
from hloc.matchers.lightglue import LightGlue
from hloc import match_features


class HLocEngine:
    def __init__(self):
        self.device = "cuda" if torch.cuda.is_available() else "cpu"

        print("Loading NetVLAD...")
        self.netvlad = NetVLAD({
            "model_name": "VGG16-NetVLAD-Pitts30K",
            "whiten": True,
        }).eval().to(self.device)

        print("Loading SuperPoint...")
        self.superpoint = SuperPoint({
            "nms_radius": 3,
            "max_keypoints": 4096,
        }).eval().to(self.device)

        print("Loading LightGlue...")
        lg_conf = dict(match_features.confs["superpoint+lightglue"]["model"])
        self.lightglue = LightGlue(lg_conf).eval().to(self.device)

        print("All models loaded ✅")

import torch

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
            "whiten": True
        }).eval().to(self.device)

        print("Loading SuperPoint...")
        self.superpoint = SuperPoint({
            "nms_radius": 3,
            "max_keypoints": 4096,
        }).eval().to(self.device)

        print("Loading LightGlue...")
        lg_conf = match_features.confs["superpoint+lightglue"]["model"]

        # ⚠️ 注意：要 deepcopy，否则 conf.pop 会污染全局
        lg_conf = dict(lg_conf)

        self.lightglue = LightGlue(lg_conf).eval().to(self.device)

        print("All models loaded ✅")

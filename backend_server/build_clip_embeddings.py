from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


def build_embeddings(
    topology_path: Path,
    image_root: Path,
    output_path: Path,
    model_name: str = "ViT-B-32",
    pretrained: str = "openai",
) -> None:
    import torch
    import open_clip

    with open(topology_path, "r", encoding="utf-8") as f:
        topology = json.load(f)

    device = "cuda" if torch.cuda.is_available() else "cpu"
    model, _, preprocess = open_clip.create_model_and_transforms(model_name, pretrained=pretrained)
    model = model.to(device)
    model.eval()

    nodes: list[str] = []
    embeddings: list[np.ndarray] = []
    missing: list[str] = []

    with torch.no_grad():
        for node_name, node in topology["nodes"].items():
            ref_image = node.get("ref_image")
            if not ref_image:
                missing.append(f"{node_name}: <no ref_image>")
                continue

            image_path = image_root / ref_image
            if not image_path.exists():
                missing.append(f"{node_name}: {image_path}")
                continue

            image = preprocess(Image.open(image_path).convert("RGB")).unsqueeze(0).to(device)
            features = model.encode_image(image)
            features = features / features.norm(dim=-1, keepdim=True)
            nodes.append(node_name)
            embeddings.append(features.cpu().numpy()[0].astype("float32"))

    if not embeddings:
        raise RuntimeError(f"No image embeddings were generated. Check image_root={image_root}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    np.savez(
        output_path,
        nodes=np.array(nodes, dtype=object),
        image_embeddings=np.stack(embeddings, axis=0),
        model_name=np.array(model_name),
        pretrained=np.array(pretrained),
    )

    print(f"[CLIP] saved {len(nodes)} embeddings -> {output_path}")
    if missing:
        print("[CLIP] missing images:")
        for item in missing:
            print(f"  - {item}")


def main() -> None:
    root = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Precompute CLIP image embeddings for topology ref_image nodes.")
    parser.add_argument("--topology", type=Path, default=root / "topology.json")
    parser.add_argument("--image-root", type=Path, default=root / "hloc" / "datasets" / "library")
    parser.add_argument("--output", type=Path, default=root / "semantic_clip_embeddings.npz")
    parser.add_argument("--model-name", default="ViT-B-32")
    parser.add_argument("--pretrained", default="openai")
    args = parser.parse_args()

    build_embeddings(args.topology, args.image_root, args.output, args.model_name, args.pretrained)


if __name__ == "__main__":
    main()

from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from typing import Any

try:
    import numpy as np
except Exception:  # pragma: no cover - numpy may be absent in minimal envs
    np = None


EMBEDDINGS_PATH = Path(__file__).resolve().parent / "semantic_clip_embeddings.npz"


@dataclass
class CandidateScore:
    node: str
    score: float
    clip_score: float | None
    text_score: float
    alias_bonus: float
    name_score: float


def _normalize(text: str) -> str:
    return (text or "").strip().lower()


def _tokens(text: str) -> set[str]:
    text = _normalize(text)
    ascii_tokens = re.findall(r"[a-z0-9]+", text)
    cjk_chars = re.findall(r"[\u4e00-\u9fff]", text)
    cjk_bigrams = [text[i : i + 2] for i in range(max(0, len(text) - 1))]
    return set(ascii_tokens + cjk_chars + cjk_bigrams)


def _jaccard(a: set[str], b: set[str]) -> float:
    if not a or not b:
        return 0.0
    return len(a & b) / len(a | b)


def _contains_score(query: str, terms: list[str]) -> float:
    query_n = _normalize(query)
    best = 0.0
    for term in terms:
        term_n = _normalize(term)
        if not term_n:
            continue
        if query_n == term_n:
            best = max(best, 1.0)
        elif term_n in query_n or query_n in term_n:
            best = max(best, min(0.95, 0.45 + 0.08 * len(term_n)))
    return best


def _expand_query_for_clip(query: str) -> str:
    query_n = _normalize(query)
    hints = []
    keyword_prompts = [
        (("厕所", "卫生间", "洗手间", "toilet", "restroom", "bathroom", "wc"), "a restroom or toilet sign in an indoor public building"),
        (("讨论", "交流", "会议", "组会", "小组", "作业", "discussion", "meeting"), "a discussion area with tables and seats for group work"),
        (("学习", "自习", "安静", "看书", "study", "quiet", "reading"), "a quiet study area for reading and individual work"),
        (("休息", "等人", "座位", "放松", "rest", "lounge"), "a rest area or lounge with seats"),
        (("楼梯", "上下楼", "stairs", "stair"), "a stair entrance in an indoor corridor"),
        (("出口", "安全", "消防", "逃生", "exit", "fire"), "a fire exit or emergency exit door"),
        (("展览", "展区", "展板", "展柜", "历史", "参观", "exhibition"), "an exhibition area with display boards and cabinets"),
    ]
    for keys, prompt in keyword_prompts:
        if any(key in query_n for key in keys):
            hints.append(prompt)
    if hints:
        return query + ". " + ". ".join(hints)
    return query


@lru_cache(maxsize=1)
def _load_clip_backend() -> dict[str, Any] | None:
    if np is None or not EMBEDDINGS_PATH.exists():
        return None
    try:
        import torch
        import open_clip

        data = np.load(EMBEDDINGS_PATH, allow_pickle=True)
        nodes = [str(x) for x in data["nodes"].tolist()]
        image_embeddings = data["image_embeddings"].astype("float32")
        model_name = str(data.get("model_name", "ViT-B-32"))
        pretrained = str(data.get("pretrained", "openai"))

        model, _, _ = open_clip.create_model_and_transforms(model_name, pretrained=pretrained)
        tokenizer = open_clip.get_tokenizer(model_name)
        device = "cuda" if torch.cuda.is_available() else "cpu"
        model = model.to(device)
        model.eval()

        return {
            "torch": torch,
            "model": model,
            "tokenizer": tokenizer,
            "device": device,
            "nodes": nodes,
            "image_embeddings": image_embeddings,
            "model_name": model_name,
            "pretrained": pretrained,
        }
    except Exception as exc:
        print(f"[SemanticDestination] CLIP backend disabled: {exc}")
        return None


def _clip_scores(query: str) -> dict[str, float]:
    backend = _load_clip_backend()
    if backend is None:
        return {}

    torch = backend["torch"]
    model = backend["model"]
    tokenizer = backend["tokenizer"]
    device = backend["device"]
    text = _expand_query_for_clip(query)

    with torch.no_grad():
        tokens = tokenizer([text]).to(device)
        text_features = model.encode_text(tokens)
        text_features = text_features / text_features.norm(dim=-1, keepdim=True)
        text_vec = text_features.cpu().numpy()[0].astype("float32")

    scores = backend["image_embeddings"] @ text_vec
    return {node: float(score) for node, score in zip(backend["nodes"], scores)}


def _load_topology(topology_path: str | Path) -> dict[str, Any]:
    with open(topology_path, "r", encoding="utf-8") as f:
        return json.load(f)


def _node_text(node_name: str, node: dict[str, Any]) -> str:
    aliases = " ".join(str(x) for x in node.get("aliases", []))
    return " ".join(
        [
            node_name,
            str(node.get("category", "")),
            aliases,
            str(node.get("description", "")),
            str(node.get("ref_image", "")),
        ]
    )


def _score_node(query: str, query_tokens: set[str], node_name: str, node: dict[str, Any], clip_score: float | None) -> CandidateScore:
    aliases = [str(x) for x in node.get("aliases", [])]
    alias_bonus = _contains_score(query, aliases)
    name_score = _contains_score(query, [node_name, node_name.replace("_", " ")])
    text_score = _jaccard(query_tokens, _tokens(_node_text(node_name, node)))

    preference = _default_preference(query, node_name, str(node.get("category", "")))

    if clip_score is None:
        score = 0.50 * alias_bonus + 0.30 * text_score + 0.20 * name_score
    else:
        # CLIP cosine is usually in a narrow range, so map it gently to [0, 1].
        clip_norm = max(0.0, min(1.0, (clip_score + 1.0) / 2.0))
        score = 0.45 * clip_norm + 0.40 * max(text_score, name_score) + 0.15 * alias_bonus
        clip_score = clip_norm
    score += preference

    return CandidateScore(
        node=node_name,
        score=float(score),
        clip_score=None if clip_score is None else float(clip_score),
        text_score=float(max(text_score, name_score)),
        alias_bonus=float(alias_bonus),
        name_score=float(name_score),
    )


def _default_preference(query: str, node_name: str, category: str) -> float:
    query_n = _normalize(query)
    generic_intents = [
        (("讨论", "交流", "会议", "组会", "小组", "作业", "discussion", "meeting"), "discussion", "Discussion Area 2"),
        (("学习", "自习", "安静", "看书", "study", "quiet", "reading"), "study", "Study Area 2"),
        (("休息", "等人", "座位", "放松", "rest", "lounge"), "rest_area", "Rest Area 1"),
    ]
    for keys, intent_category, preferred_node in generic_intents:
        if category == intent_category and node_name == preferred_node and any(key in query_n for key in keys):
            return 0.03
    return 0.0


def resolve_destination(query: str, topology_path: str | Path, topk: int = 3) -> dict[str, Any]:
    topology = _load_topology(topology_path)
    nodes: dict[str, dict[str, Any]] = topology["nodes"]
    query_clean = (query or "").strip()
    if not query_clean:
        raise ValueError("Destination query is empty")

    if query_clean in nodes:
        exact = CandidateScore(query_clean, 1.0, None, 1.0, 1.0, 1.0)
        return _format_result(query_clean, exact, [exact], "exact_node_name")

    lower_to_name = {name.lower(): name for name in nodes}
    if query_clean.lower() in lower_to_name:
        name = lower_to_name[query_clean.lower()]
        exact = CandidateScore(name, 1.0, None, 1.0, 1.0, 1.0)
        return _format_result(query_clean, exact, [exact], "exact_node_name_case_insensitive")

    clip = _clip_scores(query_clean)
    query_tokens = _tokens(query_clean)
    scores = [
        _score_node(query_clean, query_tokens, node_name, node, clip.get(node_name))
        for node_name, node in nodes.items()
    ]
    scores.sort(key=lambda item: item.score, reverse=True)
    best = scores[0]
    method = "clip_alias_description" if clip else "alias_description_fallback"

    # If every score is near zero, use a deterministic fallback instead of crashing.
    if best.score <= 0.0:
        first_name = next(iter(nodes))
        best = CandidateScore(first_name, 0.0, None, 0.0, 0.0, 0.0)
        method = "fallback_first_node"

    return _format_result(query_clean, best, scores[:topk], method)


def _format_result(query: str, best: CandidateScore, top_scores: list[CandidateScore], method: str) -> dict[str, Any]:
    return {
        "query": query,
        "destination_node": best.node,
        "score": round(best.score, 4),
        "method": method,
        "topk": [
            {
                "node": item.node,
                "score": round(item.score, 4),
                "clip_score": None if item.clip_score is None else round(item.clip_score, 4),
                "text_score": round(item.text_score, 4),
                "alias_bonus": round(item.alias_bonus, 4),
                "name_score": round(item.name_score, 4),
            }
            for item in top_scores
        ],
    }


def format_semantic_topk(result: dict[str, Any]) -> str:
    return "; ".join(f"{item['node']}:{item['score']:.2f}" for item in result.get("topk", []))


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="Resolve a natural-language destination query to a topology node.")
    parser.add_argument("query")
    parser.add_argument("--topology", default=str(Path(__file__).resolve().parent / "topology.json"))
    args = parser.parse_args()
    print(json.dumps(resolve_destination(args.query, args.topology), ensure_ascii=False, indent=2))

from __future__ import annotations

import math


def locate_regions(x: float, y: float) -> list[str]:
    """
    根据 WiFi 坐标返回 HLoc 检索前缀。
    返回值保持为“主区域 + 邻近区域”的列表。
    """

    rects = {
        "O1": (0.0, 14.5, 0.0, 11.0),
        "O2": (0.0, 7.0, 11.0, 20.0),
        "G":  (7.0, 14.5, 11.0, 20.0),
        "F1": (0.0, 7.0, 20.0, 28.0),
        "F2": (7.0, 14.5, 20.0, 28.0),
        "E1": (0.0, 7.0, 28.0, 36.3),
        "E2": (7.0, 14.5, 28.0, 36.3),
        "D1": (0.0, 7.0, 36.3, 44.7),
        "D2": (7.0, 14.5, 36.3, 44.7),
        "J":  (14.5, 23.5, 20.0, 28.0),
        "I":  (14.5, 23.5, 28.0, 36.3),
        "H":  (14.5, 23.5, 36.3, 44.7),
        "C":  (0.0, 14.5, 44.7, 54.0),
    }

    def inside(name: str) -> bool:
        xmin, xmax, ymin, ymax = rects[name]
        return xmin <= x < xmax and ymin <= y < ymax

    def dist(name: str) -> float:
        xmin, xmax, ymin, ymax = rects[name]
        dx = max(xmin - x, 0.0, x - xmax)
        dy = max(ymin - y, 0.0, y - ymax)
        return math.hypot(dx, dy)

    primary = None
    for name in rects:
        if inside(name):
            primary = "O" if name.startswith("O") else name
            break

    if primary is None:
        return []

    candidates = set()
    for name in rects:
        if dist(name) <= 2.0:
            candidates.add("O" if name.startswith("O") else name)

    if primary in {"F1", "E1", "D1"}:
        candidates -= {"F2", "E2", "D2"}
    elif primary in {"F2", "E2", "D2"}:
        candidates -= {"F1", "E1", "D1"}

    if primary == "F2":
        candidates -= {"I", "H"}
    elif primary == "E2":
        candidates -= {"J", "H"}
    elif primary == "D2":
        candidates -= {"J", "I"}

    return [primary] + sorted(candidates - {primary})

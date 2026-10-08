#!/usr/bin/env python3
"""
localization_server.py
=======================
基于 RadioMap 的 WiFi 室内定位核心模块。
复现 Anyplace 的 WKNN（加权K近邻）定位算法。

仅返回 (x, y) 坐标，不做楼层判断，不打印任何信息。

RadioMap JSON 格式（扁平结构，无楼层嵌套）：
{
    "all_bssids": ["aa:bb:cc:dd:ee:ff", ...],
    "points": [
        {"x": 1.0, "y": 2.0, "fingerprint": {"aa:bb:cc:dd:ee:ff": -65, ...}},
        ...
    ]
}
"""

from __future__ import annotations

import json
import math
import os
from typing import Any

DEFAULT_RSS = -110.0  # 未检测到 AP 时的默认 RSS 值


def _normalize_bssid(value: Any) -> str:
    return str(value).strip().lower()


def _normalize_rss(value: Any) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return DEFAULT_RSS


def _extract_aps(wifi_payload: Any) -> list[dict[str, Any]] | None:
    if isinstance(wifi_payload, list):
        return wifi_payload

    if not isinstance(wifi_payload, dict):
        return None

    for key in ("APs", "aps", "AP_LIST", "ap_list", "wifi", "scan"):
        aps = wifi_payload.get(key)
        if isinstance(aps, list):
            return aps

    return None


def _euclidean_distance(
    scan: dict[str, float],
    reference: dict[str, float],
    all_bssids: list[str],
) -> float:
    """
    计算实时扫描与 RadioMap 参考点之间的信号空间欧氏距离。

    只使用实时扫描和参考指纹都实际检测到的 AP。RadioMap 维度很多时，
    大量共同缺失的 -110 会稀释有效信号，导致 WKNN 更容易被噪声拉偏。
    """
    sum_sq = 0.0
    count = 0
    for bssid in all_bssids:
        r_scan = scan.get(bssid, DEFAULT_RSS)
        r_ref = reference.get(bssid, DEFAULT_RSS)
        if r_scan <= DEFAULT_RSS or r_ref <= DEFAULT_RSS:
            continue
        sum_sq += (r_scan - r_ref) ** 2
        count += 1

    if count == 0:
        return float("inf")

    return math.sqrt(sum_sq)


def _wknn_locate(
    scan: dict[str, float],
    points: list[dict],
    all_bssids: list[str],
    k: int = 3,
) -> tuple[float, float] | None:
    """
    WKNN 加权K近邻定位。
    """
    if not points:
        return None

    distances: list[tuple[float, dict]] = []
    for pt in points:
        fingerprint = pt.get("fingerprint", {})
        if not isinstance(fingerprint, dict):
            continue
        dist = _euclidean_distance(scan, fingerprint, all_bssids)
        if math.isinf(dist):
            continue
        distances.append((dist, pt))

    if not distances:
        return None

    distances.sort(key=lambda item: item[0])
    k_nearest = distances[: max(1, min(k, len(distances)))]

    epsilon = 1e-6
    weights = [1.0 / (d + epsilon) for d, _ in k_nearest]
    total_w = sum(weights)

    if total_w <= 0:
        return None

    pred_x = 0.0
    pred_y = 0.0
    for w, (_, pt) in zip(weights, k_nearest):
        pred_x += w * float(pt.get("x", 0.0))
        pred_y += w * float(pt.get("y", 0.0))

    pred_x /= total_w
    pred_y /= total_w

    return (round(pred_x, 3), round(pred_y, 3))


class WifiLocator:
    """
    单场景 WiFi 定位器。
    """

    def __init__(self, radiomap_path: str):
        """
        参数:
            radiomap_path: RadioMap JSON 文件路径。
        """
        if not os.path.exists(radiomap_path):
            raise FileNotFoundError(f"RadioMap 文件不存在: {radiomap_path}")

        with open(radiomap_path, "r", encoding="utf-8") as f:
            rm = json.load(f)

        if not isinstance(rm, dict):
            raise ValueError(f"RadioMap 格式错误: {radiomap_path}")

        self._points: list[dict] = rm.get("points", [])
        if not isinstance(self._points, list) or not self._points:
            raise ValueError(f"RadioMap 中 'points' 字段缺失或为空: {radiomap_path}")

        self._all_bssids: list[str] = [
            _normalize_bssid(bssid) for bssid in rm.get("all_bssids", [])
            if _normalize_bssid(bssid)
        ]

        # 兼容：如果 JSON 没给 all_bssids，则从 fingerprint 里自动汇总
        if not self._all_bssids:
            seen = set()
            merged: list[str] = []
            for pt in self._points:
                fingerprint = pt.get("fingerprint", {})
                if not isinstance(fingerprint, dict):
                    continue
                for bssid in fingerprint.keys():
                    nb = _normalize_bssid(bssid)
                    if nb and nb not in seen:
                        seen.add(nb)
                        merged.append(nb)
            self._all_bssids = merged

    def get_location(self, wifi_payload: dict, k: int = 3) -> tuple[float, float] | None:
        """
        核心定位接口。

        参数:
            wifi_payload: {"APs": [{"BSSID": "aa:bb:cc...", "rss": -65}, ...]}
            k:            KNN 的 K 值，默认 3，自动截断至 [1, 10]

        返回:
            (x, y) 元组（坐标单位与 RadioMap 一致），定位失败时返回 None。
        """
        aps = _extract_aps(wifi_payload)
        if not aps:
            return None

        scan: dict[str, float] = {}
        for ap in aps:
            if not isinstance(ap, dict):
                continue

            bssid = _normalize_bssid(
                ap.get("BSSID", ap.get("bssid", ap.get("mac", "")))
            )
            if not bssid:
                continue

            scan[bssid] = _normalize_rss(
                ap.get("rss", ap.get("RSS", ap.get("rssi", ap.get("signal", DEFAULT_RSS))))
            )

        if not scan:
            return None

        k = max(1, min(int(k), 10))
        return _wknn_locate(scan, self._points, self._all_bssids, k)

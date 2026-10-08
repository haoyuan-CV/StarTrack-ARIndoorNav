using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个 WiFi AP 的扫描信息
/// </summary>
[Serializable]
public class WifiAP
{
    public string BSSID;
    public int rss;          // 信号强度 (dBm,负数)
    public string SSID;
    public int frequency;    // 频率 (MHz),2400=2.4G, 5000+=5G

    public override string ToString()
    {
        return string.Format("{0} ({1} dBm, {2} MHz)", BSSID, rss, frequency);
    }
}

/// <summary>
/// 一次 WiFi 扫描的完整结果
/// 
/// 序列化后格式和后端 WifiLocator 期望完全一致:
///   {"APs": [{"BSSID": "...", "rss": -65}, ...]}
/// 
/// 可以直接用 ToServerJson() 发给 Python 后端
/// </summary>
[Serializable]
public class WifiScanResult
{
    public List<WifiAP> APs = new List<WifiAP>();
    public long timestamp;
    public int count;
    public string error;   // 非 null 表示扫描失败

    public bool IsSuccess => string.IsNullOrEmpty(error) && APs != null && APs.Count > 0;

    // =====================================================
    // 从 Android 端返回的 JSON 解析
    // =====================================================
    public static WifiScanResult Parse(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return FromError("empty response");
        }

        try
        {
            // JsonUtility 需要包一层,但 Android 端返回的格式已经是 {"APs": [...]}
            // 所以可以直接用
            var result = JsonUtility.FromJson<WifiScanResult>(json);
            if (result == null)
            {
                return FromError("parse returned null");
            }
            if (result.APs == null)
            {
                result.APs = new List<WifiAP>();
            }
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError("[WifiScanResult] Parse exception: " + e.Message);
            return FromError("parse exception: " + e.Message);
        }
    }

    public static WifiScanResult FromError(string msg)
    {
        return new WifiScanResult
        {
            error = msg,
            APs = new List<WifiAP>(),
            count = 0,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    // =====================================================
    // 转成发给 Python 后端的 JSON (严格匹配 WifiLocator 格式)
    // =====================================================
    public string ToServerJson()
    {
        // 只保留后端需要的字段: BSSID + rss
        var minimal = new MinimalPayload { APs = new List<MinimalAP>() };
        foreach (var ap in APs)
        {
            minimal.APs.Add(new MinimalAP { BSSID = ap.BSSID, rss = ap.rss });
        }
        return JsonUtility.ToJson(minimal);
    }

    [Serializable]
    private class MinimalPayload
    {
        public List<MinimalAP> APs;
    }

    [Serializable]
    private class MinimalAP
    {
        public string BSSID;
        public int rss;
    }
}

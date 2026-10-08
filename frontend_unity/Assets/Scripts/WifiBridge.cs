using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// WiFi 扫描插件 - Unity 侧桥接脚本
/// 
/// 使用方法:
///   1. 在场景中创建空 GameObject,命名为 "WifiBridge"
///   2. 把本脚本挂到该 GameObject 上
///   3. 其他脚本通过 WifiBridge.Instance.RequestScan(callback) 调用
/// 
/// 注意:
///   - GameObject 的名字必须和 Instance 单例保持一致(Android 端通过名字回调)
///   - 必须在 Android 真机上测试,Editor 下不会工作
/// </summary>
public class WifiBridge : MonoBehaviour
{
    // ================================================
    // 单例
    // ================================================
    public static WifiBridge Instance { get; private set; }

    // 回调委托
    public delegate void WifiScanCallback(WifiScanResult result);
    private WifiScanCallback pendingCallback;

    // 避免并发请求
    private bool isScanning = false;

    // ================================================
    // Unity 生命周期
    // ================================================
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Android 启动时主动申请一次权限
#if UNITY_ANDROID && !UNITY_EDITOR
        RequestAndroidPermissions();
#endif
    }

    // ================================================
    // 对外 API 1: 主动触发一次 WiFi 扫描
    // ================================================
    /// <summary>
    /// 触发一次 WiFi 扫描。结果通过 callback 异步返回。
    /// 安卓系统限流:4 次/2 分钟。超过会自动降级为读取缓存。
    /// </summary>
    public void RequestScan(WifiScanCallback callback)
    {
        if (isScanning)
        {
            Debug.LogWarning("[WifiBridge] Scan already in progress, ignored.");
            callback?.Invoke(WifiScanResult.FromError("busy"));
            return;
        }

        pendingCallback = callback;
        isScanning = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var pluginClass = new AndroidJavaClass("com.indoornav.wifiplugin.WifiScanPlugin"))
            using (var pluginInstance = pluginClass.CallStatic<AndroidJavaObject>("getInstance"))
            {
                pluginInstance.Call("scanWifi", gameObject.name, nameof(OnWifiResult));
            }
            // 10 秒超时保护
            StartCoroutine(TimeoutWatchdog(10f));
        }
        catch (Exception e)
        {
            Debug.LogError("[WifiBridge] RequestScan exception: " + e.Message);
            isScanning = false;
            pendingCallback?.Invoke(WifiScanResult.FromError(e.Message));
            pendingCallback = null;
        }
#else
        // Editor 下返回模拟数据,方便开发时调试
        Debug.Log("[WifiBridge] Editor mode: returning mock data.");
        StartCoroutine(MockResultCoroutine());
#endif
    }

    // ================================================
    // 对外 API 2: 只读取最近一次缓存结果(不触发新扫描,速度快)
    // ================================================
    public void RequestCachedScan(WifiScanCallback callback)
    {
        pendingCallback = callback;
        isScanning = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var pluginClass = new AndroidJavaClass("com.indoornav.wifiplugin.WifiScanPlugin"))
            using (var pluginInstance = pluginClass.CallStatic<AndroidJavaObject>("getInstance"))
            {
                pluginInstance.Call("getLastScanResult", gameObject.name, nameof(OnWifiResult));
            }
            StartCoroutine(TimeoutWatchdog(5f));
        }
        catch (Exception e)
        {
            Debug.LogError("[WifiBridge] RequestCachedScan exception: " + e.Message);
            isScanning = false;
            pendingCallback?.Invoke(WifiScanResult.FromError(e.Message));
            pendingCallback = null;
        }
#else
        StartCoroutine(MockResultCoroutine());
#endif
    }

    // ================================================
    // 对外 API 3: 验证插件是否成功加载
    // ================================================
    public string GetPluginVersion()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var pluginClass = new AndroidJavaClass("com.indoornav.wifiplugin.WifiScanPlugin"))
            using (var pluginInstance = pluginClass.CallStatic<AndroidJavaObject>("getInstance"))
            {
                return pluginInstance.Call<string>("getVersion");
            }
        }
        catch (Exception e)
        {
            return "ERROR: " + e.Message;
        }
#else
        return "Editor-Mock-1.0.0";
#endif
    }

    // ================================================
    // Android 端回调入口(方法名必须和 Java 端传入的一致)
    // ================================================
    public void OnWifiResult(string jsonPayload)
    {
        isScanning = false;
        Debug.Log("[WifiBridge] Received: " + jsonPayload);

        WifiScanResult result = WifiScanResult.Parse(jsonPayload);
        pendingCallback?.Invoke(result);
        pendingCallback = null;
    }

    // ================================================
    // 内部:权限申请
    // ================================================
    private void RequestAndroidPermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // 优先用 Unity 内置的 Permission API (比 Java 端处理更平滑)
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.FineLocation))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.FineLocation);
        }
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.CoarseLocation))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.CoarseLocation);
        }

        // Android 13+ 的 NEARBY_WIFI_DEVICES
        if (SystemInfo.operatingSystem.Contains("API-33") ||
            SystemInfo.operatingSystem.Contains("API-34"))
        {
            const string nearbyPerm = "android.permission.NEARBY_WIFI_DEVICES";
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(nearbyPerm))
            {
                UnityEngine.Android.Permission.RequestUserPermission(nearbyPerm);
            }
        }
#endif
    }

    // ================================================
    // 内部:超时看门狗
    // ================================================
    private IEnumerator TimeoutWatchdog(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (isScanning)
        {
            Debug.LogWarning("[WifiBridge] Scan timeout after " + seconds + "s");
            isScanning = false;
            pendingCallback?.Invoke(WifiScanResult.FromError("timeout"));
            pendingCallback = null;
        }
    }

    // ================================================
    // 内部:Editor 下的模拟数据
    // ================================================
    private IEnumerator MockResultCoroutine()
    {
        yield return new WaitForSeconds(0.3f);
        string mockJson = @"{
            ""APs"": [
                {""BSSID"": ""aa:bb:cc:dd:ee:01"", ""rss"": -55, ""SSID"": ""MockAP1"", ""frequency"": 2412},
                {""BSSID"": ""aa:bb:cc:dd:ee:02"", ""rss"": -68, ""SSID"": ""MockAP2"", ""frequency"": 2437},
                {""BSSID"": ""aa:bb:cc:dd:ee:03"", ""rss"": -72, ""SSID"": ""MockAP3"", ""frequency"": 5180}
            ],
            ""timestamp"": 1700000000000,
            ""count"": 3
        }";
        OnWifiResult(mockJson);
    }
}

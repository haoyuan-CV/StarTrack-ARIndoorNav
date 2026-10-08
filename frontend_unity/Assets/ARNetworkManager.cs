using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using Unity.Collections;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// ============================================================
// Data contracts aligned with the FastAPI backend response.
// ============================================================
[System.Serializable]
public class PoseData
{
    public float world_x;
    public float world_y;
    public float world_z;
    public float qx;
    public float qy;
    public float qz;
    public float qw;
}

[System.Serializable]
public class PathPoint
{
    public float x;
    public float y;
    public float z;
}

[System.Serializable]
public class NavResponse
{
    public string status;
    public string message;
    public PoseData current_pose;
    public PathPoint[] navigation_path;
    public string diagnostics;
}


// ============================================================
// 涓绘帶鍒跺櫒
// ============================================================
public class ARNetworkManager : MonoBehaviour
{
    [Header("鍚庣閰嶇疆")]
    public string backendUrl = "http://s08.bupt.cc:8000/api/navigate";

    [Tooltip("HTTP 璇锋眰瓒呮椂绉掓暟")]
    public int httpTimeoutSeconds = 60;

    [Header("UI 缁勪欢")]
    public TMP_InputField destinationInput;
    public GameObject uiPanel;
    public GameObject loadingIndicator;   // Optional indicator shown during scan/upload.

    [Header("瀵艰埅鏍稿績")]
    [Header("Runtime Navigation UI")]
    public bool createRuntimeNavigationUi = true;
    public float statusMessageSeconds = 3f;

    public ARNavigator arNavigator;

    [Header("Camera Capture")]
    [Tooltip("Upload the raw ARFoundation CPU camera image instead of a Unity screen capture.")]
    public bool useARCameraCpuImage = false;
    [Tooltip("Allow fallback to Unity ScreenCapture when AR camera CPU image is unavailable.")]
    public bool allowScreenCaptureFallback = true;
    [Tooltip("Send camera intrinsics only when the uploaded image comes from ARCamera CPU image.")]
    public bool sendTrueIntrinsics = true;
    [Tooltip("ARFoundation camera manager used to capture the raw CPU camera image and true intrinsics for HLoc.")]
    public ARCameraManager arCameraManager;
    [Tooltip("JPG quality for the raw AR camera frame uploaded to HLoc.")]
    [Range(50, 100)]
    public int cameraJpegQuality = 85;

    [Header("Coordinate Scale")]
    [Tooltip("Scale factor from COLMAP SfM units to metric Unity/ARCore units.")]
    public float colmapScaleFactor = 4.45f;

    [Header("Coordinate Calibration")]
    public bool forceSafeAlignmentDefaultsOnStart = true;
    public bool useFloorYawCorrection = false;
    public float floorYawOffsetDegrees = 0f;
    public bool useYawOnlyAlignment = false;

    [Header("璋冭瘯")]
    [Tooltip("Editor 涓嬩娇鐢ㄧ殑妯℃嫙 WiFi JSON,鏂逛究涓嶈繛鐪熸満涔熻兘鑱旇皟")]
    public string editorMockWifiJson =
        "{\"APs\":[{\"BSSID\":\"aa:bb:cc:dd:ee:01\",\"rss\":-55},{\"BSSID\":\"aa:bb:cc:dd:ee:02\",\"rss\":-68}]}";

    // Runtime state.
    private string currentDestination;
    private string pendingWifiJson = null;   // Android 鍥炶皟鍐欏叆,Update 娑堣垂
    private bool isScanning = false;
    private bool isBusy = false;             // 鍏ㄥ眬鑺傛祦:鎵弿 + 涓婁紶鏈熼棿绂佹浜屾瑙﹀彂
    private float scanStartTime = 0f;        // 鎵弿瓒呮椂鍏滃簳

    // Android 鎻掍欢瀹炰緥(鍗曚緥鍙ユ焺)
    private GameObject resetButtonObject;
    private GameObject statusToastObject;
    private TMP_Text statusToastText;
    private Coroutine statusToastCoroutine;

    private AndroidJavaObject pluginInstance;

    // WiFi scan timeout in seconds.
    private const float SCAN_TIMEOUT = 10f;

    private struct CameraCapturePayload
    {
        public byte[] imageBytes;
        public int width;
        public int height;
        public float fx;
        public float fy;
        public float cx;
        public float cy;
        public string cameraModel;
        public bool hasTrueIntrinsics;
    }


    // ============================================================
    // Unity 鐢熷懡鍛ㄦ湡
    // ============================================================
    void Awake()
    {
        // The GameObject name must match the callback target used by the Android AAR.
        // This makes UnitySendMessage route WiFi scan results back to this script.
        if (gameObject.name != "ARNetworkManager")
        {
            Debug.LogWarning($"[ARNetworkManager] Current GameObject name is '{gameObject.name}'. Rename it to 'ARNetworkManager' to keep Android callbacks stable.");
        }
    }

    void Start()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

        NavTrace.Clear();

        // Unity serializes public Inspector values. Old scenes may still keep 15s here,
        // which is too short for a cold HLoc request on the edge server.
        if (httpTimeoutSeconds < 45)
        {
            Debug.LogWarning($"[ARNetworkManager] httpTimeoutSeconds={httpTimeoutSeconds}s is too short; using 60s for HLoc navigation requests.");
            httpTimeoutSeconds = 60;
        }

        if (forceSafeAlignmentDefaultsOnStart)
        {
            useYawOnlyAlignment = false;
            useFloorYawCorrection = false;
            floorYawOffsetDegrees = 0f;
        }

        Debug.Log($"[AlignmentConfig] yawOnly={useYawOnlyAlignment}, floorCorrection={useFloorYawCorrection}, floorYawOffset={floorYawOffsetDegrees:F2}, scale={colmapScaleFactor:F3}");
        NavTrace.Log($"AlignmentConfig yawOnly={useYawOnlyAlignment}, floorCorrection={useFloorYawCorrection}, floorYawOffset={floorYawOffsetDegrees:F2}, scale={colmapScaleFactor:F3}");
        TryBindARCameraManager();

#if UNITY_ANDROID && !UNITY_EDITOR
        InitPlugin();
        RequestRuntimePermissions();
#endif
        SetupRuntimeNavigationUi();
        if (arNavigator != null)
        {
            arNavigator.OnDestinationReached += HandleDestinationReached;
        }
    }

    void Update()
    {
        // Consume WiFi scan results posted back from the Android callback.
        if (!string.IsNullOrEmpty(pendingWifiJson))
        {
            string json = pendingWifiJson;
            pendingWifiJson = null;
            isScanning = false;

            Debug.Log($"[FlowDebug] Update consuming WiFi result, bytes={json.Length}");
            NavTrace.Log($"Update consuming WiFi result, bytes={json.Length}");
            OnWifiScanReceived(json);
        }

        // 鎵弿瓒呮椂淇濇姢
        if (isScanning && Time.realtimeSinceStartup - scanStartTime > SCAN_TIMEOUT)
        {
            Debug.LogWarning("[ARNetworkManager] WiFi 鎵弿瓒呮椂,鎭㈠ UI");
            isScanning = false;
            isBusy = false;
            ShowUiPanel(true);
            ShowLoading(false);
        }
    }


    // ============================================================
    // Initialize the Android WiFi scan plugin.
    // ============================================================
    private void InitPlugin()
    {
        try
        {
            // The AAR exposes a singleton: com.indoornav.wifiplugin.WifiScanPlugin.
            using (var pluginClass =
                new AndroidJavaClass("com.indoornav.wifiplugin.WifiScanPlugin"))
            {
                pluginInstance = pluginClass.CallStatic<AndroidJavaObject>("getInstance");
            }

            string version = pluginInstance.Call<string>("getVersion");
            Debug.Log($"[ARNetworkManager] WifiScanPlugin initialized, version: {version}");
        }
        catch (Exception e)
        {
            Debug.LogError("[ARNetworkManager] WifiScanPlugin initialization failed: " + e.Message);
            pluginInstance = null;
        }
    }

    private void RequestRuntimePermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
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

        // Android 13+ requires NEARBY_WIFI_DEVICES.
        const string nearby = "android.permission.NEARBY_WIFI_DEVICES";
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(nearby))
        {
            UnityEngine.Android.Permission.RequestUserPermission(nearby);
        }
#endif
    }


    // ============================================================
    // UI event: user taps Start Navigation.
    // ============================================================
    public void OnClickStartNav()
    {
        NavTrace.Log("OnClickStartNav entered.");

        if (isBusy)
        {
            Debug.LogWarning("[ARNetworkManager] Navigation request ignored because another request is running.");
            NavTrace.Log("OnClickStartNav ignored: busy.");
            return;
        }
        if (destinationInput == null || string.IsNullOrEmpty(destinationInput.text))
        {
            Debug.LogWarning("[ARNetworkManager] Destination is empty.");
            NavTrace.Log("OnClickStartNav ignored: destination empty.");
            return;
        }
        if (arNavigator == null)
        {
            Debug.LogError("[ARNetworkManager] arNavigator is not assigned. Please bind it in Inspector.");
            NavTrace.Log("OnClickStartNav failed: arNavigator missing.");
            return;
        }

        currentDestination = destinationInput.text.Trim();
        NavTrace.Log($"OnClickStartNav accepted destination={currentDestination}");
        isBusy = true;

        ShowUiPanel(false);
        ShowLoading(true);

        TriggerWifiScan();
    }

    private void TriggerWifiScan()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (pluginInstance == null)
        {
            Debug.LogError("[ARNetworkManager] WiFi plugin is not initialized. Trying to initialize again.");
            InitPlugin();
            if (pluginInstance == null)
            {
                OnScanFlowFailed("WiFi plugin unavailable");
                return;
            }
        }

        try
        {
            isScanning = true;
            scanStartTime = Time.realtimeSinceStartup;

            // New plugin API: scanWifi(gameObjectName, methodName).
            // Android calls UnityPlayer.UnitySendMessage after the scan finishes.
            // The callback payload is a JSON string.
            pluginInstance.Call("scanWifi", gameObject.name, nameof(OnWifiResult));
        }
        catch (Exception e)
        {
            Debug.LogError("[ARNetworkManager] 璋冪敤 scanWifi 澶辫触: " + e.Message);
            OnScanFlowFailed("璋冪敤鎵弿鎺ュ彛澶辫触");
        }
#else
        // Editor or non-Android platform: use mock data.
        Debug.Log("[ARNetworkManager] Editor 妯″紡,浣跨敤妯℃嫙 WiFi 鏁版嵁");
        pendingWifiJson = editorMockWifiJson;
#endif
    }


    // ============================================================
    // Android 鍥炶皟鍏ュ彛
    // Method name must match nameof(OnWifiResult) used in TriggerWifiScan.
    // 鈿狅笍 UnityPlayer.UnitySendMessage 鏄粠 Android 绾跨▼璋冭繃鏉ョ殑,
    // Unity routes this callback back onto the main thread.
    // ============================================================
    public void OnWifiResult(string jsonPayload)
    {
        Debug.Log($"[ARNetworkManager] Received WiFi scan result ({jsonPayload?.Length ?? 0} bytes)");
        NavTrace.Log($"WifiResult bytes={jsonPayload?.Length ?? 0}");
        // Pass the Android callback payload to Update so Unity-side work stays on the main flow.
        pendingWifiJson = string.IsNullOrEmpty(jsonPayload) ? "{\"APs\":[]}" : jsonPayload;
        Debug.Log($"[FlowDebug] WiFi result queued for Update, bytes={pendingWifiJson.Length}");
        NavTrace.Log($"WifiResult queued bytes={pendingWifiJson.Length}");
    }


    // ============================================================
    // 澶勭悊鎵弿缁撴灉 + 鍙戣捣涓婁紶
    // ============================================================
    private void OnWifiScanReceived(string json)
    {
        // Detect plugin errors, for example {"error":"...","APs":[]}.
        if (json.Contains("\"error\""))
        {
            Debug.LogWarning("[ARNetworkManager] 鎵弿杩斿洖閿欒: " + json);
            OnScanFlowFailed("WiFi scan failed. Check permissions and location service.");
            return;
        }

        // If APs is empty, there is no need to send a backend request.
        if (!json.Contains("\"APs\"") || json.Contains("\"APs\":[]"))
        {
            Debug.LogWarning("[ARNetworkManager] 鎵弿缁撴灉涓虹┖");
            OnScanFlowFailed("No WiFi access points were scanned. Please move closer to an AP.");
            return;
        }

        Debug.Log("[ARNetworkManager] WiFi 鏁版嵁鏈夋晥,杩涘叆鎴浘 + 涓婁紶娴佺▼");
        NavTrace.Log("Wifi scan valid, start capture and post.");
        StartCoroutine(CaptureAndPostData(currentDestination, json));
    }


    // ============================================================
    // 鎴浘 + 涓婁紶
    // ============================================================
    private IEnumerator CaptureAndPostData(string destination, string wifiJson)
    {
        // Wait until the current frame is rendered so the hidden UI state takes effect before capture.
        yield return new WaitForEndOfFrame();

        if (arNavigator == null || arNavigator.arCamera == null)
        {
            Debug.LogError("[ARNetworkManager] arCamera is not assigned.");
            OnScanFlowFailed("AR 鐩告満鏈垵濮嬪寲");
            yield break;
        }

        // Save the AR camera matrix at capture time; it anchors HLoc coordinates to AR space.
        Matrix4x4 savedARMatrix = arNavigator.arCamera.localToWorldMatrix;

        // Capture image for HLoc. Device navigation should use the raw camera CPU image;
        // screen capture is a last-resort debug path because it includes UI/curtain overlays.
        CameraCapturePayload capture = default;
        string captureError = null;
        bool captureOk = false;

        if (useARCameraCpuImage)
        {
            yield return StartCoroutine(CaptureCameraFrameCoroutine((ok, payload, errorMessage) =>
            {
                captureOk = ok;
                capture = payload;
                captureError = errorMessage;
            }));
        }
        else
        {
            captureOk = TryCaptureFallbackScreenFrame(out capture, out captureError);
        }

        if (!captureOk && useARCameraCpuImage)
        {
            if (!allowScreenCaptureFallback)
            {
                Debug.LogError("[ARNetworkManager] ARCamera CPU capture failed and screen fallback is disabled: " + captureError);
                NavTrace.Log("ARCamera CPU capture failed; screen fallback disabled: " + captureError);
                OnScanFlowFailed("Raw camera capture failed");
                yield break;
            }

            Debug.LogWarning("[ARNetworkManager] ARCamera CPU capture failed, falling back to screen capture. This debug fallback may include UI/curtain overlays: " + captureError);
            NavTrace.Log("ARCamera CPU capture failed, fallback to screen capture with overlays: " + captureError);

            CameraCapturePayload fallbackPayload;
            string fallbackError;
            bool fallbackOk = TryCaptureFallbackScreenFrame(out fallbackPayload, out fallbackError);
            if (!fallbackOk)
            {
                Debug.LogError("[ARNetworkManager] Camera frame capture failed: " + fallbackError);
                NavTrace.Log("Camera frame capture failed: " + fallbackError);
                OnScanFlowFailed("Camera frame capture failed");
                yield break;
            }

            capture = fallbackPayload;
            captureOk = true;
        }

        if (!captureOk)
        {
            Debug.LogError("[ARNetworkManager] Camera frame capture failed: " + captureError);
            NavTrace.Log("Camera frame capture failed: " + captureError);
            OnScanFlowFailed("Camera frame capture failed");
            yield break;
        }

        byte[] imageBytes = capture.imageBytes;
        if (imageBytes == null || imageBytes.Length == 0)
        {
            OnScanFlowFailed("鎴浘涓虹┖");
            yield break;
        }

        // Build request form.
        WWWForm form = new WWWForm();
        form.AddField("destination_node", destination);
        form.AddField("wifi_data_str", wifiJson);
        if (sendTrueIntrinsics && capture.hasTrueIntrinsics)
        {
            form.AddField("camera_model", capture.cameraModel);
            form.AddField("image_width", capture.width);
            form.AddField("image_height", capture.height);
            form.AddField("fx", capture.fx.ToString("R", CultureInfo.InvariantCulture));
            form.AddField("fy", capture.fy.ToString("R", CultureInfo.InvariantCulture));
            form.AddField("cx", capture.cx.ToString("R", CultureInfo.InvariantCulture));
            form.AddField("cy", capture.cy.ToString("R", CultureInfo.InvariantCulture));
        }
        string imageName = "capture_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                           + "_" + UnityEngine.Random.Range(0, 1000000).ToString("D6", CultureInfo.InvariantCulture)
                           + ".jpg";
        form.AddBinaryData("image", imageBytes, imageName, "image/jpeg");

        Debug.Log($"[RequestDebug] destination={destination}, imageName={imageName}, wifiJsonBytes={wifiJson.Length}, imageBytes={imageBytes.Length}");
        Debug.Log($"[CameraIntrinsics] source={(capture.hasTrueIntrinsics ? "ARCameraCPU" : "ScreenCapture")}, sent={(sendTrueIntrinsics && capture.hasTrueIntrinsics)}, model={capture.cameraModel}, width={capture.width}, height={capture.height}, fx={capture.fx:F3}, fy={capture.fy:F3}, cx={capture.cx:F3}, cy={capture.cy:F3}");
        Debug.Log($"[CaptureDebug] width={capture.width}, height={capture.height}, jpgBytes={imageBytes.Length}, quality={cameraJpegQuality}");
        Debug.Log($"[RequestDebug] POST {backendUrl}, timeout={httpTimeoutSeconds}s");
        NavTrace.Log($"CameraIntrinsics source={(capture.hasTrueIntrinsics ? "ARCameraCPU" : "ScreenCapture")}, sent={(sendTrueIntrinsics && capture.hasTrueIntrinsics)}, model={capture.cameraModel}, width={capture.width}, height={capture.height}, fx={capture.fx:F3}, fy={capture.fy:F3}, cx={capture.cx:F3}, cy={capture.cy:F3}");
        NavTrace.Log($"RequestDebug destination={destination}, imageName={imageName}, wifiJsonBytes={wifiJson.Length}, imageBytes={imageBytes.Length}, backend={backendUrl}, timeout={httpTimeoutSeconds}s");

        float requestStartTime = Time.realtimeSinceStartup;
        using (UnityWebRequest www = UnityWebRequest.Post(backendUrl, form))
        {
            www.timeout = httpTimeoutSeconds;
            yield return www.SendWebRequest();

            float requestElapsed = Time.realtimeSinceStartup - requestStartTime;
            Debug.Log($"[ResponseTiming] elapsed={requestElapsed:F2}s, code={www.responseCode}, error={www.error}");
            NavTrace.Log($"ResponseTiming elapsed={requestElapsed:F2}s, code={www.responseCode}, error={www.error}");

#if UNITY_2020_2_OR_NEWER
            bool httpOk = www.result == UnityWebRequest.Result.Success;
#else
            bool httpOk = !www.isNetworkError && !www.isHttpError;
#endif

            if (!httpOk)
            {
                string errorBody = www.downloadHandler != null ? www.downloadHandler.text : "";
                Debug.LogError($"[ARNetworkManager] HTTP failed: error={www.error}, code={www.responseCode}, elapsed={requestElapsed:F2}s, body={TrimForLog(errorBody, 1600)}");
                Debug.LogError($"[ARNetworkManager] 璇锋眰澶辫触: {www.error} (code={www.responseCode}, elapsed={requestElapsed:F2}s)");
                NavTrace.Log($"HTTP failed error={www.error}, code={www.responseCode}, elapsed={requestElapsed:F2}s, body={TrimForLog(errorBody, 400)}");
                OnScanFlowFailed("缃戠粶璇锋眰澶辫触: " + www.error);
                yield break;
            }

            string rawResponse = www.downloadHandler.text;
            Debug.Log($"[ResponseDebug] raw={TrimForLog(rawResponse, 1600)}");
            NavTrace.Log($"ResponseDebug raw={TrimForLog(rawResponse, 800)}");

            NavResponse responseData;
            try
            {
                responseData = JsonUtility.FromJson<NavResponse>(rawResponse);
            }
            catch (Exception e)
            {
                Debug.LogError("[ARNetworkManager] 瑙ｆ瀽鍝嶅簲澶辫触: " + e.Message +
                               "\n鍘熷鍐呭: " + rawResponse);
                OnScanFlowFailed("鍝嶅簲鏍煎紡閿欒");
                yield break;
            }

            if (responseData == null || responseData.status != "success")
            {
                string msg = responseData?.message ?? "Unknown backend error";
                Debug.LogError("[ARNetworkManager] 鍚庣杩斿洖澶辫触: " + msg);
                OnScanFlowFailed("鍚庣澶勭悊澶辫触: " + msg);
                yield break;
            }

            if (responseData.current_pose == null || responseData.navigation_path == null ||
                responseData.navigation_path.Length == 0)
            {
                Debug.LogError("[ARNetworkManager] Backend response is missing pose or path.");
                OnScanFlowFailed("Navigation data is incomplete");
                yield break;
            }

            Debug.Log($"[BackendDiagnostics] {responseData.diagnostics}");
            Debug.Log($"[WifiZone] {responseData.diagnostics}");
            Debug.Log($"[AlignmentConfig] yawOnly={useYawOnlyAlignment}, floorCorrection={useFloorYawCorrection}, floorYawOffset={floorYawOffsetDegrees:F2}, scale={colmapScaleFactor:F3}");
            NavTrace.Log($"BackendDiagnostics {responseData.diagnostics}");
            NavTrace.Log($"AlignmentConfig yawOnly={useYawOnlyAlignment}, floorCorrection={useFloorYawCorrection}, floorYawOffset={floorYawOffsetDegrees:F2}, scale={colmapScaleFactor:F3}");

            // ===== 鍧愭爣瀵归綈 (V3: COLMAP -> metric -> ARSession) =====
            // HLoc backend returns camera center and camera-to-world rotation in the COLMAP world.
            // The frontend only converts COLMAP units to metric units, then aligns that metric
            // COLMAP space to the current ARSession space using the captured AR camera pose.
            Vector3 camPosColmap = new Vector3(
                responseData.current_pose.world_x,
                responseData.current_pose.world_y,
                responseData.current_pose.world_z
            );
            Quaternion R_cam2world_colmap = new Quaternion(
                responseData.current_pose.qx,
                responseData.current_pose.qy,
                responseData.current_pose.qz,
                responseData.current_pose.qw
            ).normalized;

            Vector3 camForwardColmap = R_cam2world_colmap * Vector3.forward;
            Vector3 camPosMetric = ColmapToMetricPosition(camPosColmap);
            Vector3 camForwardMetric = ColmapToMetricDirection(camForwardColmap);
            camForwardMetric.y = 0f;

            if (camForwardMetric.sqrMagnitude < 0.0001f)
            {
                camForwardMetric = Vector3.forward;
            }
            else
            {
                camForwardMetric.Normalize();
            }

            Quaternion camRotMetric = Quaternion.LookRotation(camForwardMetric, Vector3.up);
            Matrix4x4 M_hloc_metric = Matrix4x4.TRS(camPosMetric, camRotMetric, Vector3.one);

            // 3. Alignment matrix: map metric COLMAP coordinates into Unity AR world space.
            //    At capture time, the same camera has a pose in both coordinate systems:
            //      M_ar = savedARMatrix         (camera pose in Unity AR world)
            //      M_hloc_metric               (camera pose in metric COLMAP world)
            //    We solve M_ar = alignMatrix * M_hloc_metric, so:
            //      alignMatrix = M_ar * M_hloc_metric.inverse
            Vector3 savedARPos = new Vector3(savedARMatrix.m03, savedARMatrix.m13, savedARMatrix.m23);
            Vector3 arForwardRaw = new Vector3(savedARMatrix.m02, savedARMatrix.m12, savedARMatrix.m22);
            Vector3 arForwardYaw = arForwardRaw;
            arForwardYaw.y = 0f;

            if (arForwardYaw.sqrMagnitude < 0.0001f)
            {
                arForwardYaw = Vector3.forward;
            }
            else
            {
                arForwardYaw.Normalize();
            }

            Quaternion arYawRot = Quaternion.LookRotation(arForwardYaw, Vector3.up);
            Matrix4x4 M_ar_yaw_only = Matrix4x4.TRS(savedARPos, arYawRot, Vector3.one);
            Matrix4x4 alignMatrix = useYawOnlyAlignment
                ? M_ar_yaw_only * M_hloc_metric.inverse
                : savedARMatrix * M_hloc_metric.inverse;

            Debug.Log($"[CoordDebug] C_colmap={camPosColmap}");
            Debug.Log($"[CoordDebug] C_metric={camPosMetric}, scale={colmapScaleFactor:F3}");
            Debug.Log($"[CoordDebug] camForwardColmap={camForwardColmap}");
            Debug.Log($"[CoordDebug] camForwardMetric={camForwardMetric}");
            Debug.Log($"[CoordDebug] savedAR_pos={savedARMatrix.GetColumn(3)}");
            Debug.Log($"[CoordDebug] alignMatrix={alignMatrix}");
            Debug.Log($"[WifiDebug] diagnostics={responseData.diagnostics}");
            Debug.Log($"[HLocPose] C_colmap={camPosColmap}, q_cam2world=({R_cam2world_colmap.x:F5}, {R_cam2world_colmap.y:F5}, {R_cam2world_colmap.z:F5}, {R_cam2world_colmap.w:F5})");
            Debug.Log($"[AxisDebug] hlocForwardColmap={camForwardColmap}, hlocForwardMetricYaw={camForwardMetric}");
            Debug.Log($"[ARPose] savedAR_pos={savedARPos}, arForwardRaw={arForwardRaw}, arForwardYaw={arForwardYaw}");
            Debug.Log($"[FloorCorrection] enabled={useFloorYawCorrection}, yawOffsetDeg={floorYawOffsetDegrees:F2}, yawOnlyAlignment={useYawOnlyAlignment}");
            Debug.Log($"[AlignDebug] M_hloc_metric={M_hloc_metric}");
            Debug.Log($"[AlignDebug] M_ar_yaw_only={M_ar_yaw_only}");
            Debug.Log($"[AlignDebug] M_ar_full={savedARMatrix}");
            Debug.Log($"[AlignDebug] alignMatrix={alignMatrix}");
            NavTrace.Log($"HLocPose C_colmap={camPosColmap}, q_cam2world=({R_cam2world_colmap.x:F5}, {R_cam2world_colmap.y:F5}, {R_cam2world_colmap.z:F5}, {R_cam2world_colmap.w:F5})");
            NavTrace.Log($"ARPose savedAR_pos={savedARPos}, arForwardRaw={arForwardRaw}, arForwardYaw={arForwardYaw}");
            NavTrace.Log($"AlignDebug alignMatrix={alignMatrix}");

            List<Vector3> pathList = new List<Vector3>(responseData.navigation_path.Length);
            for (int i = 0; i < responseData.navigation_path.Length; i++)
            {
                var pt = responseData.navigation_path[i];
                Vector3 pColmap = new Vector3(pt.x, pt.y, pt.z);
                Vector3 pMetric = ColmapToMetricPosition(pColmap);
                Debug.Log($"[PathInputDebug] node[{i}] colmap={pColmap} metric={pMetric}");
                Debug.Log($"[RouteNode] index={i}, colmap={pColmap}, metric={pMetric}");
                NavTrace.Log($"RouteNode index={i}, colmap={pColmap}, metric={pMetric}");
                pathList.Add(pMetric);
            }

            // 4. 鍚姩瀵艰埅
            Debug.Log($"[ARNetworkManager] Alignment complete, navigation starts. Path points={pathList.Count}");
            NavTrace.Log($"StartNavigation pathCount={pathList.Count}");
            arNavigator.StartNavigation(pathList, alignMatrix);
            ShowRuntimeNavigationControls(true);
            ShowStatusMessage("NAVIGATION STARTED");

            // 娴佺▼瀹屾垚
            ShowLoading(false);
            isBusy = false;
        }
    }


    // ============================================================
    // COLMAP 鍧愭爣 -> 绫冲埗瀵艰埅鍧愭爣
    // ============================================================
    private Vector3 ColmapToMetricPosition(Vector3 pColmap)
    {
        Vector3 metric = new Vector3(
            pColmap.x * colmapScaleFactor,
            -pColmap.y * colmapScaleFactor,
            pColmap.z * colmapScaleFactor
        );

        return ApplyFloorYawCorrection(metric);
    }

    private Vector3 ColmapToMetricDirection(Vector3 dirColmap)
    {
        Vector3 metricDir = new Vector3(
            dirColmap.x,
            -dirColmap.y,
            dirColmap.z
        );

        return ApplyFloorYawCorrection(metricDir);
    }

    private Vector3 ApplyFloorYawCorrection(Vector3 value)
    {
        if (!useFloorYawCorrection || Mathf.Abs(floorYawOffsetDegrees) < 0.0001f)
        {
            return value;
        }

        return Quaternion.Euler(0f, floorYawOffsetDegrees, 0f) * value;
    }

    private bool TryBindARCameraManager()
    {
        if (arCameraManager != null)
        {
            return true;
        }

        if (arNavigator != null && arNavigator.arCamera != null)
        {
            arCameraManager = arNavigator.arCamera.GetComponent<ARCameraManager>();
        }

        if (arCameraManager == null)
        {
            arCameraManager = FindObjectOfType<ARCameraManager>();
        }

        if (arCameraManager == null)
        {
            Debug.LogWarning("[ARNetworkManager] ARCameraManager not found. Android HLoc capture requires ARFoundation CPU image support.");
            NavTrace.Log("ARCameraManager not found.");
            return false;
        }

        return true;
    }

    private IEnumerator CaptureCameraFrameCoroutine(Action<bool, CameraCapturePayload, string> onComplete)
    {
#if UNITY_EDITOR
        CameraCapturePayload fallbackPayload;
        string fallbackError;
        bool fallbackOk = TryCaptureFallbackScreenFrame(out fallbackPayload, out fallbackError);
        onComplete?.Invoke(fallbackOk, fallbackPayload, fallbackError);
        yield break;
#else
        CameraCapturePayload payload = default;

        if (!TryBindARCameraManager())
        {
            onComplete?.Invoke(false, payload, "ARCameraManager is not bound.");
            yield break;
        }

        if (!arCameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
        {
            onComplete?.Invoke(false, payload, "ARCameraManager.TryGetIntrinsics failed.");
            yield break;
        }

        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            onComplete?.Invoke(false, payload, "ARCameraManager.TryAcquireLatestCpuImage failed.");
            yield break;
        }

        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
            outputFormat = TextureFormat.RGB24,
            transformation = XRCpuImage.Transformation.None
        };

        var conversion = cpuImage.ConvertAsync(conversionParams);
        cpuImage.Dispose();

        while (!conversion.status.IsDone())
        {
            yield return null;
        }

        if (conversion.status != XRCpuImage.AsyncConversionStatus.Ready)
        {
            string error = "XRCpuImage async conversion failed: " + conversion.status;
            conversion.Dispose();
            onComplete?.Invoke(false, payload, error);
            yield break;
        }

        Texture2D texture = null;
        try
        {
            int width = conversion.conversionParams.outputDimensions.x;
            int height = conversion.conversionParams.outputDimensions.y;
            NativeArray<byte> rawData = conversion.GetData<byte>();

            texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.LoadRawTextureData(rawData);
            texture.Apply(false);

            float scaleX = intrinsics.resolution.x > 0 ? (float)width / intrinsics.resolution.x : 1f;
            float scaleY = intrinsics.resolution.y > 0 ? (float)height / intrinsics.resolution.y : 1f;

            payload = new CameraCapturePayload
            {
                imageBytes = texture.EncodeToJPG(Mathf.Clamp(cameraJpegQuality, 50, 100)),
                width = width,
                height = height,
                fx = intrinsics.focalLength.x * scaleX,
                fy = intrinsics.focalLength.y * scaleY,
                cx = intrinsics.principalPoint.x * scaleX,
                cy = intrinsics.principalPoint.y * scaleY,
                cameraModel = "PINHOLE",
                hasTrueIntrinsics = true
            };

            bool ok = payload.imageBytes != null && payload.imageBytes.Length > 0;
            onComplete?.Invoke(ok, payload, ok ? null : "Encoded JPG is empty.");
        }
        catch (Exception e)
        {
            onComplete?.Invoke(false, payload, e.Message);
        }
        finally
        {
            conversion.Dispose();

            if (texture != null)
            {
                Destroy(texture);
            }
        }
#endif
    }

    private bool TryCaptureFallbackScreenFrame(out CameraCapturePayload payload, out string error)
    {
        payload = default;
        error = null;

        Texture2D screenTex = null;
        try
        {
            screenTex = ScreenCapture.CaptureScreenshotAsTexture();
            if (screenTex == null)
            {
                error = "ScreenCapture returned null.";
                return false;
            }

            int width = screenTex.width;
            int height = screenTex.height;
            float fov = 70f * Mathf.Deg2Rad;
            float focal = width / (2f * Mathf.Tan(fov / 2f));

            payload = new CameraCapturePayload
            {
                imageBytes = screenTex.EncodeToJPG(Mathf.Clamp(cameraJpegQuality, 50, 100)),
                width = width,
                height = height,
                fx = focal,
                fy = focal,
                cx = width / 2f,
                cy = height / 2f,
                cameraModel = "PINHOLE",
                hasTrueIntrinsics = false
            };

            return payload.imageBytes != null && payload.imageBytes.Length > 0;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
        finally
        {
            if (screenTex != null)
            {
                Destroy(screenTex);
            }
        }
    }

    private string TrimForLog(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        return text.Substring(0, maxChars) + "...<truncated>";
    }

    public void ResetNavigationFlow()
    {
        Debug.Log("[ARNetworkManager] Reset navigation flow: stop current nav, clear request state, return to start panel.");

        StopAllCoroutines();
        isScanning = false;
        isBusy = false;
        pendingWifiJson = null;

        if (arNavigator != null)
        {
            arNavigator.StopNavigation();
        }

        ShowLoading(false);
        ShowUiPanel(true);
        ShowRuntimeNavigationControls(false);
        ShowStatusMessage("READY FOR NEW ROUTE");
    }

    private void HandleDestinationReached()
    {
        Debug.Log("[ARNetworkManager] Destination reached.");
        NavTrace.Log("Destination reached.");
        isScanning = false;
        isBusy = false;
        pendingWifiJson = null;

        ShowLoading(false);
        ShowRuntimeNavigationControls(false);
        ShowUiPanel(true);
        ShowStatusMessage("DESTINATION REACHED");
    }

    private void SetupRuntimeNavigationUi()
    {
        if (!createRuntimeNavigationUi)
        {
            return;
        }

        Canvas canvas = null;
        if (uiPanel != null)
        {
            canvas = uiPanel.GetComponentInParent<Canvas>(true);
        }
        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }
        if (canvas == null)
        {
            Debug.LogWarning("[ARNetworkManager] No Canvas found, runtime navigation UI will not be created.");
            return;
        }

        if (resetButtonObject == null)
        {
            resetButtonObject = CreateRuntimeButton(canvas.transform);
            resetButtonObject.SetActive(false);
        }

        if (statusToastObject == null)
        {
            statusToastObject = CreateStatusToast(canvas.transform);
            statusToastObject.SetActive(false);
        }
    }

    private GameObject CreateRuntimeButton(Transform parent)
    {
        GameObject root = new GameObject("Runtime_ResetNavigationButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.70f, 0.91f);
        rect.anchorMax = new Vector2(0.96f, 0.975f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = root.GetComponent<Image>();
        image.color = new Color(0.05f, 0.18f, 0.22f, 0.72f);

        Button button = root.GetComponent<Button>();
        button.onClick.AddListener(ResetNavigationFlow);

        GameObject label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(root.transform, false);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TMP_Text text = label.GetComponent<TMP_Text>();
        text.text = "RESET";
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 30f;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(0.78f, 0.98f, 1f, 1f);

        return root;
    }

    private GameObject CreateStatusToast(Transform parent)
    {
        GameObject root = new GameObject("Runtime_StatusToast", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.10f, 0.78f);
        rect.anchorMax = new Vector2(0.90f, 0.86f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = root.GetComponent<Image>();
        image.color = new Color(0.02f, 0.12f, 0.15f, 0.82f);

        GameObject label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(root.transform, false);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(20f, 0f);
        labelRect.offsetMax = new Vector2(-20f, 0f);

        statusToastText = label.GetComponent<TMP_Text>();
        statusToastText.text = "";
        statusToastText.alignment = TextAlignmentOptions.Center;
        statusToastText.fontSize = 34f;
        statusToastText.fontStyle = FontStyles.Bold;
        statusToastText.color = Color.white;

        return root;
    }

    private void ShowRuntimeNavigationControls(bool show)
    {
        if (resetButtonObject != null)
        {
            resetButtonObject.SetActive(show);
        }
    }

    private void ShowStatusMessage(string message)
    {
        SetupRuntimeNavigationUi();
        if (statusToastObject == null || statusToastText == null)
        {
            return;
        }

        if (statusToastCoroutine != null)
        {
            StopCoroutine(statusToastCoroutine);
        }

        statusToastText.text = message;
        statusToastObject.SetActive(true);
        statusToastCoroutine = StartCoroutine(HideStatusMessageAfterDelay());
    }

    private IEnumerator HideStatusMessageAfterDelay()
    {
        yield return new WaitForSeconds(statusMessageSeconds);
        if (statusToastObject != null)
        {
            statusToastObject.SetActive(false);
        }
        statusToastCoroutine = null;
    }


    // ============================================================
    // 澶辫触璺緞鐨勭粺涓€澶勭悊
    // ============================================================
    private void OnScanFlowFailed(string reason)
    {
        Debug.LogWarning("[ARNetworkManager] 娴佺▼澶辫触: " + reason);
        NavTrace.Log($"Flow failed: {reason}");
        isScanning = false;
        isBusy = false;
        pendingWifiJson = null;

        ShowUiPanel(true);
        ShowLoading(false);
        ShowRuntimeNavigationControls(false);
    }


    // ============================================================
    // UI 杈呭姪鏂规硶
    // ============================================================
    private void ShowUiPanel(bool show)
    {
        if (uiPanel != null) uiPanel.SetActive(show);
    }

    private void ShowLoading(bool show)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(show);
    }
}

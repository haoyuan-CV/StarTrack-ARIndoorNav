using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WiFi 扫描示例 - 演示如何调用 WifiBridge
/// 
/// 使用方法:
///   1. 场景中创建 Canvas,放一个 Button 和 Text
///   2. 把本脚本挂到任意 GameObject 上
///   3. 在 Inspector 里把 Button 和 Text 拖到对应字段
///   4. 运行,点击按钮即可看到扫描结果
/// </summary>
public class WifiScanExample : MonoBehaviour
{
    [Header("UI References")]
    public Button scanButton;
    public Text resultText;

    void Start()
    {
        if (scanButton != null)
        {
            scanButton.onClick.AddListener(OnScanClicked);
        }

        // 打印插件版本号,验证 AAR 加载成功
        if (WifiBridge.Instance != null)
        {
            string version = WifiBridge.Instance.GetPluginVersion();
            Debug.Log("[Example] Plugin version: " + version);
            if (resultText != null)
            {
                resultText.text = "Plugin loaded: " + version + "\nClick to scan.";
            }
        }
        else
        {
            Debug.LogError("[Example] WifiBridge.Instance is null! " +
                "Make sure WifiBridge script is attached to a GameObject named 'WifiBridge'.");
        }
    }

    private void OnScanClicked()
    {
        if (resultText != null) resultText.text = "Scanning...";

        WifiBridge.Instance.RequestScan(OnScanCompleted);
    }

    private void OnScanCompleted(WifiScanResult result)
    {
        if (!result.IsSuccess)
        {
            string msg = "Scan failed: " + (result.error ?? "unknown");
            Debug.LogWarning(msg);
            if (resultText != null) resultText.text = msg;
            return;
        }

        // 成功:打印详情
        Debug.Log(string.Format("[Example] Got {0} APs", result.count));
        for (int i = 0; i < result.APs.Count && i < 5; i++)
        {
            Debug.Log("  " + result.APs[i]);
        }

        // 显示到 UI
        if (resultText != null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format("Found {0} APs:", result.count));
            int shown = Mathf.Min(result.APs.Count, 10);
            for (int i = 0; i < shown; i++)
            {
                sb.AppendLine(result.APs[i].ToString());
            }
            resultText.text = sb.ToString();
        }

        // ========================================================
        // 关键:把结果转成后端期望格式,发给定位服务
        // ========================================================
        string serverPayload = result.ToServerJson();
        Debug.Log("[Example] Server payload: " + serverPayload);

        // 示例:发给 Python 后端
        // StartCoroutine(SendToLocationServer(serverPayload));
    }

    // 示例:HTTP POST 到后端
    /*
    private IEnumerator SendToLocationServer(string jsonPayload)
    {
        using (var req = new UnityWebRequest("http://your-server:8000/locate", "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Location response: " + req.downloadHandler.text);
            }
        }
    }
    */
}

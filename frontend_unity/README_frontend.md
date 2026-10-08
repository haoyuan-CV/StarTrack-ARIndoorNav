# StarTrack Unity 前端说明

## 工程信息

- Unity 版本: 2022.3.62f3c1
- 目标平台: Android
- 主要功能:
  - 采集手机 WiFi 扫描结果
  - 使用 ARCamera CPU Image 获取真实摄像头图像
  - 将目的地、WiFi 信息和图像上传至后端
  - 接收后端导航路径并在 AR 场景中显示方向箭头

## 打开方式

1. 使用 Unity Hub 打开 `frontend_unity` 目录。
2. 等待 Unity 根据 `Packages/manifest.json` 自动恢复依赖。
3. 打开场景 `Assets/Scenes/SampleScene.unity`。
4. 切换到 Android 平台后构建 APK。

## 主要源码

- `Assets/ARNetworkManager.cs`: 前后端通信、导航请求、ARCamera 图像采集与上传
- `Assets/ARNavigator.cs`: AR 导航箭头显示逻辑
- `Assets/NavTrace.cs`: 导航轨迹/路径显示相关逻辑
- `Assets/Scripts/WifiBridge.cs`: Android WiFi 扫描桥接
- `Assets/Scripts/WifiScanExample.cs`: WiFi 扫描调用示例
- `Assets/Scripts/WelcomeScreen.cs`: 前端界面入口
- `Assets/Plugins/Android/WifiScanPlugin.aar`: Android WiFi 扫描插件

## 主导航流程

```text
WelcomeScreen.OnClickStart()
  -> ARNetworkManager.OnClickStartNav()
  -> TriggerWifiScan()
  -> OnWifiResult(json)
  -> CaptureAndPostData(destination, wifiJson)
  -> UnityWebRequest.Post(backendUrl, form)
  -> ARNavigator.StartNavigation(pathList, alignMatrix)
```

更完整的系统结构说明见上一级目录的 `ARCHITECTURE.md`。

## 注意事项

实际导航演示依赖后端服务和五楼对应的视觉/WiFi 数据。在非建图区域测试时，前端可以启动，但后端无法返回有效定位和路径。

# StarTrack 代码结构与调用关系

我们将系统拆分为前端、后端和演示数据三部分提交。`backend_server/` 对应项目工程中的 `backend/cloud_server/wifi_hloc` 主后端目录。下面说明各部分代码的职责、调用链路和运行依赖，便于快速了解 StarTrack 从用户输入目的地到 AR 箭头导航的完整过程。

## 1. 系统总体流程

StarTrack 是一个端云协同的室内 AR 导航系统。手机端负责采集用户输入、WiFi 扫描结果和 ARCamera 图像；服务器端负责 WiFi 粗定位、HLoc 视觉定位、自然语言目的地解析和拓扑寻路；最后手机端把返回路径渲染成 AR 箭头。

```text
用户输入目的地
  -> Unity 前端隐藏欢迎幕布
  -> Android WiFi 插件扫描 AP
  -> ARFoundation 获取 ARCamera CPU Image 与相机内参
  -> HTTP multipart/form-data 上传到 FastAPI 后端
  -> 后端 WiFi WKNN 粗定位
  -> 根据 WiFi 位置筛选候选区域 prefix
  -> HLoc 常驻进程进行视觉定位
  -> semantic_destination.py 将自然语言目的地解析为拓扑节点
  -> topology.json 中查找最近起点和解析后的目的地
  -> 返回 current_pose + navigation_path
  -> Unity 计算 COLMAP/AR 坐标对齐矩阵
  -> ARNavigator 渲染逐点导航箭头
```

## 2. 前端代码说明

前端目录：`frontend_unity/`

| 文件 | 作用 |
| --- | --- |
| `Assets/Scenes/SampleScene.unity` | Unity 主场景，挂载 AR 相机、UI、导航脚本等对象 |
| `Assets/ARNetworkManager.cs` | 前端主流程控制器：读取自然语言目的地、触发 WiFi 扫描、采集相机图像、上传后端、解析路径结果、启动 AR 导航 |
| `Assets/ARNavigator.cs` | AR 导航渲染器：根据后端返回路径和对齐矩阵，在用户前方显示方向箭头并切换下一个目标点 |
| `Assets/NavTrace.cs` | 本地调试日志：把关键导航流程写入 Android 应用私有目录，便于排查现场问题 |
| `Assets/Scripts/WelcomeScreen.cs` | 欢迎页/目的地输入 UI 的视觉样式与按钮逻辑 |
| `Assets/Scripts/WifiBridge.cs` | Android WiFi 扫描插件的封装类，可独立请求扫描并处理回调 |
| `Assets/Scripts/WifiScanResult.cs` | WiFi 扫描 JSON 的数据结构与后端格式转换 |
| `Assets/Scripts/WifiScanExample.cs` | WiFi 插件调用示例，不是主导航链路的核心入口 |
| `Assets/Plugins/Android/WifiScanPlugin.aar` | Android 原生 WiFi 扫描插件 |

### 前端核心调用链

```text
WelcomeScreen.OnClickStart()
  -> ARNetworkManager.OnClickStartNav()
  -> TriggerWifiScan()
  -> Android AAR scanWifi(...)
  -> ARNetworkManager.OnWifiResult(json)
  -> OnWifiScanReceived(json)
  -> CaptureAndPostData(destination, wifiJson)
  -> CaptureCameraFrameCoroutine(...)
  -> UnityWebRequest.Post(backendUrl, form)
  -> Parse NavResponse
  -> ColmapToMetricPosition(...)
  -> alignMatrix = savedARMatrix * M_hloc_metric.inverse
  -> ARNavigator.StartNavigation(pathList, alignMatrix)
```

### 前端与后端接口

默认请求地址在 `Assets/ARNetworkManager.cs` 中：

```text
http://s08.xxxx.cc:8000/api/navigate
```

请求方式为 `POST multipart/form-data`，主要字段：

| 字段 | 来源 | 说明 |
| --- | --- | --- |
| `destination_node` | 用户输入框 | 用户目的地输入，可为标准节点名或自然语言描述 |
| `wifi_data_str` | Android WiFi 插件 | WiFi AP 列表 JSON |
| `image` | ARCamera CPU Image | 当前摄像头图像，用于 HLoc 视觉定位 |
| `fx/fy/cx/cy/width/height` | ARFoundation 相机内参 | 用于服务端按真实相机参数定位，若服务端暂未使用也可保留 |

后端返回字段：

| 字段 | 说明 |
| --- | --- |
| `status` | `success` 或 `error` |
| `message` | 人类可读提示 |
| `current_pose` | HLoc 输出的当前相机位置与姿态 |
| `navigation_path` | 拓扑寻路后的路径点列表 |
| `diagnostics` | WiFi 区域、定位结果等调试信息 |

## 3. 后端代码说明

后端目录：`backend_server/`

| 文件/目录 | 作用 |
| --- | --- |
| `full_backend_pipeline.py` | FastAPI 主入口，整合 WiFi、HLoc 和导航路径规划 |
| `wifi_loc.py` | WiFi 指纹定位模块，使用 WKNN 计算粗略二维位置 |
| `prefix.py` | 根据 WiFi 粗定位位置输出候选区域，用于缩小视觉定位搜索范围 |
| `semantic_destination.py` | 自然语言目的地解析模块，融合 CLIP 图文相似度、节点别名和文本描述匹配 |
| `build_clip_embeddings.py` | 离线构建节点参考图像 CLIP 向量的脚本 |
| `navigator.py` | 加载 `topology.json`，寻找最近拓扑节点，并计算到目的地的最短路径 |
| `topology.json` | 室内导航拓扑图，保存节点坐标、参考图像、aliases、category、description 和边关系 |
| `radiomap.json` | WiFi 指纹地图，保存 AP 列表与参考点 RSS 指纹 |
| `requirements.txt` | 后端 Python 依赖列表 |
| `hloc/` | HLoc 视觉定位源码和本项目调用脚本 |

### 后端核心调用链

```text
uvicorn full_backend_pipeline:app
  -> lifespan()
     -> 初始化 WifiLocator(radiomap.json)
     -> 启动 hloc/scripts/loc_service.py 常驻进程
  -> POST /api/navigate
     -> 保存前端上传图像到 hloc/datasets/query/
     -> locator.locate(wifi_data)
     -> locate_regions(wifi_x, wifi_y)
     -> call_hloc(image_name, prefixes)
     -> 读取 HLoc 返回位姿 C = -R^T t
     -> resolve_destination(destination_node, topology.json)
     -> get_navigation_path(C, resolved_destination, topology.json)
     -> 返回 current_pose 和 navigation_path
```

## 4. HLoc 与数据目录

源码包保留了 HLoc 代码，但没有内置完整视觉地图数据。原因是视觉地图属于演示运行数据，体积较大，通常应作为单独数据包或网盘链接提交。

完整运行时，演示数据需按以下路径放回：

```text
backend_server/hloc/datasets/library/      # 参考图像库
backend_server/hloc/datasets/query/        # 查询图像目录，可运行时自动生成
backend_server/hloc/outputs/library/       # HLoc/COLMAP 建图输出
backend_server/hloc/cache/                 # 模型权重与缓存
```

源码包中已保留轻量级数据：

```text
backend_server/topology.json
backend_server/radiomap.json
backend_server/semantic_clip_embeddings.npz
```

## 5. 坐标关系

后端 HLoc 返回的是 COLMAP 世界坐标系下的相机位姿。前端需要把路径点转换到 Unity AR 世界中，因此使用：

```text
C_colmap = -R^T t
P_metric = scale * P_colmap
P_AR = M_align * P_metric
```

在本项目中，`ARNetworkManager.cs` 会在拍照瞬间保存 AR 相机矩阵 `savedARMatrix`，并结合后端返回的 HLoc 相机矩阵计算：

```text
alignMatrix = savedARMatrix * M_hloc_metric.inverse
```

此矩阵用于把后端路径点映射到手机当前 AR 会话空间，使 `ARNavigator.cs` 可以在真实画面中显示方向箭头。

## 6. 复现与目录索引

源码结构可按以下顺序阅读：

```text
README.md
ARCHITECTURE.md
frontend_unity/README_frontend.md
backend_server/README_backend.md
demo_data/dataset_manifest.md
```

完整导航演示依赖 `demo_data/dataset_manifest.md` 中列出的 HLoc 演示数据包；手机端需在对应建图区域内采集图像，才能完成视觉定位和 AR 路径引导。

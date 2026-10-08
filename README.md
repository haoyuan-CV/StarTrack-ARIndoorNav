# StarTrack 
 
StarTrack（星轨）面向大型室内空间导航需求，利用现有WiFi基础设施与普通手机摄像头，实现低成本、可落地的AR室内导航。系统融合WiFi指纹粗定位、HLoc视觉精定位、自然语言目的地解析与语义拓扑寻路，支持用户以自然语言输入目的地，并在手机端获得实景AR箭头引导。相比依赖BLE/UWB等专用硬件的方案，本作品部署成本低、改造难度小，适用于智慧校园、图书馆、医院、商场等场景。

本仓库包含 StarTrack 室内 AR 导航系统的前端 Unity 工程源码、后端定位与导航服务源码、自然语言目的地解析代码，以及演示数据说明。

其中 `backend_server/` 对应本地项目工程中的 `backend/cloud_server/wifi_hloc` 主后端目录，包含当前版本的 WiFi 定位、HLoc 调用、自然语言目的地解析和拓扑导航代码。

阅读建议：

1. `ARCHITECTURE.md`: 先了解整体系统、前后端调用链和坐标关系。
2. `frontend_unity/README_frontend.md`: 查看 Unity 前端工程结构和主要脚本。
3. `backend_server/README_backend.md`: 查看后端接口、依赖和运行方式。
4. `demo_data/dataset_manifest.md`: 查看未直接放入源码包的大体积演示数据说明。

## 目录结构

```text
StarTrack_Submission/
  frontend_unity/     Unity Android/AR 前端工程
  backend_server/     Python 后端服务、WiFi 定位、HLoc 调用、语义目的地解析与拓扑导航代码
  demo_data/          演示数据说明，不直接内置大体积视觉地图数据
  docs/               可放置项目书、答辩 PPT、演示视频链接等材料
  ARCHITECTURE.md     系统结构、调用链、坐标关系说明
```

## 前端环境

- Unity: 2022.3.62f3c1
- 平台: Android
- 主要依赖: AR Foundation, ARCore, TextMeshPro, UGUI

打开方式：使用 Unity Hub 打开 `frontend_unity` 目录。

## 后端环境

- Python: 建议 3.10
- Web 框架: FastAPI + Uvicorn
- 定位与导航模块: WiFi 指纹 WKNN + HLoc 视觉定位 + 语义目的地解析 + 拓扑路径规划

后端入口文件：`backend_server/full_backend_pipeline.py`

## 数据说明

源码包中保留了轻量级运行配置和语义拓扑数据：

- `backend_server/topology.json`: 室内语义拓扑图，包含节点坐标、aliases、category、description、ref_image
- `backend_server/radiomap.json`: WiFi 指纹地图
- `backend_server/semantic_destination.py`: 自然语言目的地解析逻辑
- `backend_server/build_clip_embeddings.py`: CLIP 节点图像向量构建脚本
- `backend_server/semantic_clip_embeddings.npz`: 已构建的 CLIP 节点图像语义向量

HLoc 视觉定位所需的大体积数据没有直接放入源码包，包括参考图像库、COLMAP/HLoc 特征文件、匹配文件、三维模型和模型权重。这些文件属于演示运行数据或缓存结果，体积较大，故未作为源码直接提交。CLIP 语义向量文件体积较小，已随源码包提交。

# StarTrack 后端说明

## 功能概述

后端负责接收 Unity 前端上传的导航请求，并完成以下流程：

1. 解析目的地、WiFi 扫描结果和摄像头图像。
2. 使用 `radiomap.json` 完成 WiFi 指纹粗定位。
3. 调用 HLoc 视觉定位服务估计当前相机位姿。
4. 使用 `semantic_destination.py` 将自然语言目的地解析为 `topology.json` 中的标准节点。
5. 结合 `topology.json` 查找起点、终点和拓扑路径。
6. 返回前端可用于 AR 箭头导航的路径结果。

## 入口文件

```bash
uvicorn full_backend_pipeline:app --host 0.0.0.0 --port 8000
```

接口地址：

```text
POST /api/navigate
```

请求字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `image` | file | 前端上传的 ARCamera 图像 |
| `wifi_data_str` | form string | WiFi 扫描 JSON |
| `destination_node` | form string | 目的地输入，可为标准节点名，也可为“我想找个讨论的地方”等自然语言描述 |

返回字段：

| 字段 | 说明 |
| --- | --- |
| `status` | 请求状态 |
| `message` | 状态说明 |
| `current_pose` | 当前相机位姿 |
| `navigation_path` | 拓扑路径点 |
| `diagnostics` | 调试信息 |

## 主要源码

- `full_backend_pipeline.py`: FastAPI 服务入口，负责请求处理、HLoc 常驻进程管理和结果融合
- `wifi_loc.py`: WiFi 指纹 WKNN 定位模块
- `prefix.py`: 根据 WiFi/区域先验筛选候选区域
- `semantic_destination.py`: 自然语言目的地解析模块，融合 CLIP 图文相似度、节点别名和文本描述匹配
- `build_clip_embeddings.py`: 离线构建节点参考图像 CLIP 向量的脚本
- `navigator.py`: 基于拓扑图的路径规划逻辑
- `topology.json`: 室内语义拓扑图，保存节点坐标、边、aliases、category、description、ref_image 等字段
- `radiomap.json`: WiFi 指纹地图
- `hloc/`: HLoc 视觉定位源码及必要脚本

更完整的调用链说明见上一级目录的 `ARCHITECTURE.md`。

## Python 依赖

建议使用 Python 3.10，并安装：

```bash
pip install -r requirements.txt
```

也可以参考 `hloc/requirements.txt` 安装 HLoc 相关依赖。

## 语义目的地解析

自然语言目的地解析由 `semantic_destination.py` 完成。它首先检查用户输入是否直接命中拓扑节点名或 aliases；随后结合节点 `description/category/ref_image` 与 CLIP 图像向量进行综合评分，输出最终的 `destination_node`，供 `navigator.py` 执行 Dijkstra 寻路。

CLIP 图像向量可通过以下命令离线生成：

```bash
python build_clip_embeddings.py --topology topology.json --image-root hloc/datasets/library --output semantic_clip_embeddings.npz
```

源码包已随附 `semantic_clip_embeddings.npz`。该文件由 `build_clip_embeddings.py` 根据 `topology.json` 中每个节点的 `ref_image` 离线生成，用于启用 CLIP 图文语义相似度。没有该文件时，系统仍可基于节点名称、aliases 和 description 进行文本匹配。

## 运行数据

源码包未内置完整 HLoc 视觉地图数据。直接运行视觉定位时，需要将演示数据包中的目录放回：

```text
backend_server/hloc/datasets/library/
backend_server/hloc/datasets/query/
backend_server/hloc/outputs/library/
backend_server/hloc/cache/
```

其中 `datasets/query/` 可由前端请求时动态写入，但 `datasets/library/` 和 `outputs/library/` 是视觉定位必须依赖的参考库和建图结果。

## 编码说明

提交包内后端顶层 Python 文件均为 UTF-8 编码，避免中文注释导致跨环境导入失败。

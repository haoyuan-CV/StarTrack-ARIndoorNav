# StarTrack 演示文件

本目录保存用于项目展示的 Android 演示应用和实机导航录像。

## 文件说明

| 文件 | 内容 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| `StarTrack-Android-demo.apk` | Unity + ARCore 导航演示应用 | 50.59 MiB | `B3114314469DCCB2334050CE771B551B5406BAAF65CB4373298E2992098589FB` |
| `StarTrack-navigation-demo.mp4` | 图书馆场景实机 AR 导航录像 | 6.71 MiB | `E3A4A76DF313E5D0C8B00C506D77016FA321B7E7304B754AB148D2288200B2F6` |
| `demo-preview.jpg` | README 视频预览图 | 0.04 MiB | `A4B08EA87E6566911706F896B5FB080C26FB3E0272333F544B1CA9EFF26F4CE5` |

演示视频时长约 77 秒，分辨率为 432 × 960，展示了目的地输入、AR 路径箭头引导和到达提示。

## 安装说明

APK 是项目验收阶段保留的 ARM64 Android 演示构建，最低 Android API Level 为 26（Android 8.0）。设备还需要支持 ARCore，并授予相机和 WiFi 相关权限。

可使用 ADB 安装：

```bash
adb install -r StarTrack-Android-demo.apk
```

应用依赖项目部署时使用的后端定位服务。若原公网服务已经停止，APK 仍可用于查看界面和安装包结构，但完整在线定位流程需要重新部署 `backend_server/` 并更新前端服务地址。


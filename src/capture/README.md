# M01 屏幕采集模块

当前实施原则：直接使用开源采集引擎，只编写项目身份、回调、生命周期和统计的薄适配。程序不保存/上传屏幕像素。

## 已接入的运行路径

`windows_capture_adapter.py` 直接依赖 `windows-capture==2.0.1`。上游负责 WGC、显示器选择、捕获线程与原生缓冲；本模块提供每路身份/epoch、单调帧序列、回调失败与停止控制。调用方在 consumer 回调内使用 `FramePacket.frame`，不将原帧保留到回调外。

Python 绑定会将 GPU 帧映射为 CPU 可读内存。本路径用于已安装库的功能验证和算法探索，不宣称 GPU 全链路零复制。[Rust 消费者](../capture-rust/README.md)已直接接通上游 `Frame::as_raw_texture()` / `device()` / `device_context()`，单屏GPU复制及完成查询通过，无屏幕像素读回。生产生命周期和静态统计继续补齐，WGC 后端由上游维护。

依赖版本见根 `requirements-capture.txt`。当前 monitor_index 为上游 1 基索引，外部传入 displayId/epoch；验证器使用 session-only 身份。完整持久身份、动态拓扑恢复、HDR和跨显卡仍待补齐/验证。

## 对照样例

`wgc_capture.hpp/.cpp`、`frame_tracker.hpp`、`capture_probe.cpp` 是首轮 Windows SDK 原生对照样例，不是生产采集引擎的选型。已用于证明本机硬件纹理、浮点表面、生命周期与变化 ROI 能工作；暂停扩写。未来保留其测试价值或按引用清理，不维护第二套产品捕获后端。

## 构建与验证

本项目用户已授权宿主开发。现成库环境位于 `data/envs/capture`；使用 `tools/capture/verify.ps1 -Backend library -MinimumDisplays 1` 验证当前单屏，`-MinimumDisplays 2` 才检查多屏前提。真实帧源需要交互桌面；受限工具进程的绘图测试与交互桌面可能不同，不能混合结果。

原生样例由 `tools/capture/build.ps1 -Target probe` 使用已安装 MSVC/Windows SDK 构建，所有产物归 data；帧跟踪测试用 `-Target tests`。适配测试为 `tests/capture/test_adapter.py`。

维护者为 AI-pet；源码、测试定义与依赖版本随 Git 持久维护。运行数据在 data，详情见其 README。跨层契约以 docs/CONTRACTS 为语义权威，当前只完成采集第一片功能，不能标记完整 C01 或多屏验收完成。

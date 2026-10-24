# 第三方组件与采用状态

由 AI-pet 维护，随依赖锁定和复用决策更新；保留至对应组件退役及分发义务完成。完整采集研究见 docs/OPEN-SOURCE。

| 组件 | 当前用途 | 版本/许可依据 | 状态 |
|---|---|---|---|
| windows-capture | 实际 WGC 引擎，Python 验证；Rust 原始纹理接口为后续生产路径 | 2.0.1；[上游 MIT LICENCE](https://github.com/NiiightmareXD/windows-capture/blob/main/LICENCE)；私有安装环境包含许可证 | 已安装与单屏运行验证 |
| numpy | 上游 Python 帧缓冲依赖 | 2.5.4；安装发行物含许可证，分发前纳入完整声明 | 已安装，项目不复制其源码 |
| opencv-python | 上游 Python 包导入依赖 | 5.0.0.93；安装发行物含 OpenCV/第三方声明，分发前复核 | 已安装，未用其图片写盘接口 |
| Microsoft Windows SDK / MSVC | 原生对照样例编译、系统 API | 宿主已安装 SDK 10.0.26100.0 / MSVC 14.44.35207，按其安装许可使用 | 不随程序复制工具链 |

当前没有复制 VPet/eSheep 的程序或角色素材。宠物主体优先选择直接改装它们，具体版本、依赖和素材许可待主体集成包冻结。OBS/libobs、ScreenCapture.NET 为备选研究对象，未安装或嵌入；许可证分别核实后再采用，不将不同许可的源码混拷入本项目。

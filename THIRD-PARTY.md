# 第三方组件与采用状态

由 AI-pet 维护，随依赖锁定和复用决策更新；保留至对应组件退役及分发义务完成。完整采集研究见 docs/OPEN-SOURCE。

| 组件 | 当前用途 | 版本/许可依据 | 状态 |
|---|---|---|---|
| windows-capture | 实际 WGC 引擎，Python 验证；Rust 原始纹理接口为后续生产路径 | 2.0.1；[上游 MIT LICENCE](https://github.com/NiiightmareXD/windows-capture/blob/main/LICENCE)；私有安装环境包含许可证 | 已安装与单屏运行验证 |
| numpy | 上游 Python 帧缓冲依赖 | 2.5.4；安装发行物含许可证，分发前纳入完整声明 | 已安装，项目不复制其源码 |
| opencv-python | 上游 Python 包导入依赖 | 5.0.0.93；安装发行物含 OpenCV/第三方声明，分发前复核 | 已安装，未用其图片写盘接口 |
| Microsoft Windows SDK / MSVC | 原生对照样例编译、系统 API | 宿主已安装 SDK 10.0.26100.0 / MSVC 14.44.35207，按其安装许可使用 | 不随程序复制工具链 |

VPet 与 eSheep 固定源码研究副本已进入 data/upstream，未纳入项目 Git 或发布包。VPet Core 按 Apache-2.0 构建并作为选定主体；eSheep MIT Portable 用于对照验证；具体提交和最小构建适配见 docs/HOST-SELECTION。测试资源为自有合成图形，上游角色与内置图片的分发权利独立核验。

Rust 实际使用 windows-capture 2.0.1，传递依赖及校验和由 src/capture-rust/Cargo.lock 固定；工具链 Rust 1.99.1。VPet 构建使用私有 .NET SDK 8.0.426 与其 csproj 声明依赖，完整分发依赖/资源声明在主体集成门冻结。OBS/libobs、ScreenCapture.NET 仍仅为备选研究，未嵌入。

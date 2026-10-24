# 开源复用方案与研究依据

研究范围：2026-10-24—2026-10-25，公开项目 README、列明的关键源码与平台官方文档。windows-capture 已安装并完成单屏功能验证，其他候选处于资料/源码核实阶段；长期运行、性能和分发许可审计仍待完成。链接为研究来源，实施时锁定选定提交或版本、许可证与依赖。

## 选型结论

当前采用复用优先：采集直接接 windows-capture，宠物主体优先验证 VPet/eSheep 的直接嵌入或改装，复杂特效按需要接 Godot/Effekseer 等成熟运行库。避免先重建桌宠宿主、捕获后端和资源系统。静态检测与覆盖调度仅补齐缺失逻辑，组合接口保持薄层。当前运行版本与边界见 CAPTURE-STATUS。

允许从多个项目抽取并组合能力，优先适配稳定接口；每次复用记录上游、提交、路径、许可证/素材权利、改造范围、依赖成本和验收。复用粒度可以是库、源码模块或独立服务；不默认携带完整上游产品的设置、遥测、权限和数据路径。

## 候选与可复用边界

| 候选 | 已核实依据 | 建议复用 | 需补充与限制 |
|---|---|---|---|
| [NightMin2002/GodotDesktopPet](https://github.com/NightMin2002/GodotDesktopPet) | `entities/pet/pet_movement.gd`、`pet_effects.gd`、`interop/WindowsManager.cs`、`core/hit_region_manager.gd`、`project.godot` | 优先验证窗口边缘互动、物理/状态机、拖影/冲击波、电弧与穿透管理 | README 声明 MIT，但已查源码树无 LICENSE 文件；直接复制/分发前补齐许可依据。窗口交互不是静态像素识别；目标飞行与覆盖调度需新增 |
| [Adrianotiger/desktopPet](https://github.com/Adrianotiger/desktopPet) | README 的任务栏、窗口、多屏描述；`LICENSE.md` 为 MIT | 二维精灵、XML 动画配置、编辑器及桌面活动备选 | 复杂局部效果和自有调度需新增；角色素材另查来源 |
| [LorisYounger/VPet](https://github.com/LorisYounger/VPet) | `IController.cs`、`MWController.cs`；Apache-2.0 代码许可 | WPF 桌宠核心、互动、动画生态、插件和移动接口备选 | 与已有自主行为仲裁；跨身体效果、新资源契约、感知和覆盖需改装；素材分开核验 |
| [WindyWin/vpet-agent-pet](https://github.com/WindyWin/vpet-agent-pet) | `starter/src/desktop/wander.cpp`、`pet_window.h`、事件/集成文档；自有代码 Apache-2.0 | Qt 运动与攀爬结构、Agent 会话事件、任务状态表现 | 作为任务集成参考优先于主体；实际客户端版本需验收；角色素材有独立条款 |
| [DCDingCong/desktopbuddy](https://github.com/DCDingCong/desktopbuddy) | `physicsAnimator.ts`、`activeWindowService.ts`、`localEventServer.ts`；MIT | 模型接入/本机事件服务与通知思路、弹跳等运动备选 | 已读屏幕感知为活动窗口标题/进程信息，不提供静态像素识别。核实 CLI 与独立进程如何获取凭据；不能把进程环境变量当通用发现机制 |
| [effekseer/EffekseerForGodot4](https://github.com/effekseer/EffekseerForGodot4) | 官方插件 README、LICENSE（MIT）、二维限制文档 | 后续魔法效果制作与运行库 | 渲染器、透明窗口、二维深度/软粒子等能力按版本验证；素材许可逐包检查 |
| [hananel42/screen-tricks](https://github.com/hananel42/screen-tricks) | `overlay/src/win32.rs`、`overlay/src/image/capture.rs`；MIT | Windows 覆盖层、粒子/波浪/破碎设计参考 | 捕获代码采用 GDI，不作为已选 GPU 主采集实现；完整桌面破碎不等于局部低打扰效果；输入 Hook/注入能力按本项目范围裁剪 |
| [Harpeaux/ChromakeyTransparentWindow](https://github.com/Harpeaux/ChromakeyTransparentWindow) | README、LICENSE（MIT） | Godot/Win32 穿透实现参考 | 将纯黑作为透明色键，无法未经改造承担不透明纯黑遮罩；不得作为默认方案 |
| [FlaUI/FlaUI](https://github.com/FlaUI/FlaUI) | README、LICENSE.txt（MIT） | 可选 UIA 结构/焦点辅助接口 | 像素主感知保持 GPU 路线；自绘画布、跨权限边界能力依应用而定 |
| [NiiightmareXD/windows-capture](https://github.com/NiiightmareXD/windows-capture) | README、LICENCE（MIT）、`src/frame.rs` 的原始纹理/表面/设备接口 | 已安装2.0.1 Python绑定并真实单屏验证；生产原始GPU纹理复用上游Rust接口 | Python绑定有CPU映射；Rust集成/HDR/物理多屏待验证，不承诺零复制 |
| [ra1nty/DXcam](https://github.com/ra1nty/DXcam) | README 的 DXGI/WGC、区域与内存帧接口 | 隔离环境中的算法探索备选 | 不作为正式程序引入 Python 运行时的默认理由；实施前核验完整许可/版本 |

## 可组合方案

- **优先主体方案**：VPet 核心 + windows-capture + 覆盖调度适配 + 独立局部效果层。先复用互动、动画、插件与移动控制，再验证外部路线控制、行为仲裁和资源绑定；素材许可单独核验。
- **轻量精灵方案**：eSheep + 同一感知/调度接口 + 局部效果层，优先利用其多屏、任务栏/窗口活动和 XML 动画资源。针对精灵风格需求评估直接改装。
- **特效扩展方案**：在所选主体外按需加入 Effekseer/Godot 局部渲染层。只有主体验证确有不可适配缺口，才评估替换宿主；NightMin 作为许可待核实的参考候选。
- **任务优先备选**：VPet Agent Pet + 同一感知/调度服务 + 新特效层；优先保留其会话模型而非耦合整套 UI。
- **Web 技术备选**：Desktop Buddy + Canvas/Pixi 类渲染适配 + 原生感知服务；评估 Electron 常驻开销、窗口大小与特效越界。

本轮未发现能够直接复用的完整“静态暴露 + 实际遮挡/调暗时长 + 操作避让 + 有序随机覆盖”模块。候选的漫游、权重随机或普通通知不能视为已实现这些能力。

## 官方技术依据

| 来源 | 支持的决策 |
|---|---|
| [WGC 帧、HDR 与资源生命周期](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture) | 获取 D3D 表面，HDR 使用合适浮点格式；归还帧后不能保留底层表面引用 |
| [CreateForMonitor](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createformonitor) | 桌面应用按显示器句柄建立捕获项 |
| [Desktop Duplication](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api) | GPU 桌面表面、更新/移动区域；合并更新区可能含未变化像素 |
| [DXGI 多屏与设备](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutput1-duplicateoutput) | 每输出独立捕获，时间戳组合；设备需匹配输出显卡 |
| [DuplicateOutput1](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgioutput5-duplicateoutput1) | 格式协商和高色彩格式；不能把旧 BGRA8 路线当完整 HDR 方案 |
| [排除自身窗口捕获](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) | 自身动画与底层静态观察分离的候选机制；按平台/后端验证 |
| [Godot DisplayServer](https://docs.godotengine.org/en/stable/classes/class_displayserver.html) | Windows 区域穿透可同时裁画面；透明、焦点、多屏坐标按平台验证 |
| [Godot 屏幕读取着色器](https://docs.godotengine.org/en/stable/tutorials/shaders/screen-reading_shaders.html) | 引擎自身屏幕纹理不等于其他桌面应用内容 |
| [Godot 粒子](https://docs.godotengine.org/en/stable/tutorials/2d/particle_systems_2d.html) | 烟雾、魔法、粒子基础；按实际版本选择节点和性能策略 |
| [Effekseer 二维限制](https://effekseer.github.io/Help_Godot/v4/en/introduction.html) | 二维效果能力需单独校验 |
| [UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-uiautomationoverview) | 可选结构/焦点信息，不替代真实像素稳定性 |

## 采用准入与替换规则

实施包选择候选时建立组件记录：`componentId`、上游 URL、选定提交 SHA、原文件路径、许可证和素材来源、原始/修改后摘要、依赖与网络行为、数据路径、适配边界、隔离验收回执及替换条件。原始上游代码与改造说明可追溯。

所有尚未实测的性能/兼容性均为待验证项。准入检查集中在模块/集成检查点，不为每次编辑申请顾问批准；本地证据无法解决的重大许可、架构、安全或数据风险进入集中审查。

新增备选：[DarthAffe/ScreenCapture.NET](https://github.com/DarthAffe/ScreenCapture.NET) 提供按显卡/显示器/区域采集及 GPU 缩小，主公开消费接口是 CPU 图像，LGPL-2.1；[OBS libobs-winrt](https://github.com/obsproject/obs-studio/blob/master/libobs-winrt/winrt-capture.h) 已提供显示器/窗口捕获，但依赖 libobs 图形和模块体系，完整嵌入与 GPL 分发成本更高。已读取两者接口，尚未安装。成熟度、轻量性与本产品匹配度分别评估，不能仅按 stars 判定。

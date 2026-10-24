# 桌宠主体选型与接口实测

日期：2026-10-25。归属 M04/M05/M06/M09，执行入口 tools/pet-hosts/verify.ps1。按固定上游源码实际编译和运行，选择 **VPet-Simulator.Core 作为首版改装主体**，eSheep 保留为轻量精灵备选。

## 固定来源

| 项目 | 源码检查点 | 本地用途 |
|---|---|---|
| [VPet](https://github.com/LorisYounger/VPet/tree/0c2e795b8e81485e448675316ba088d5226d908d) | `0c2e795b8e81485e448675316ba088d5226d908d` | data/upstream/vpet；原版 Core 构建及引用，源码 Apache-2.0；角色与内置图片独立授权 |
| [eSheep](https://github.com/Adrianotiger/desktopPet/tree/ac4e5dab57f6e4aa9c78ee0cfba4ffff8a680ad8) | `ac4e5dab57f6e4aa9c78ee0cfba4ffff8a680ad8` | data/upstream/esheep；Portable 构建、XML/运动/子效果测试；代码 MIT，第三方角色逐包审计 |
| [windows-capture](https://github.com/NiiightmareXD/windows-capture/tree/c7d106448eb9d9b251345c39047711e1cd408ae2) | `c7d106448eb9d9b251345c39047711e1cd408ae2`，2.0.1 | 源码接口核对；实际 Rust 使用 registry 精确版本及 Cargo.lock |

上游树在 data 中保留，程序包和项目 Git 不收录整棵上游。测试直接引用源码项目/编译物。VPet 用项目私有 .NET SDK 8.0.426；eSheep 用现有 MSBuild 与私有 net472 引用程序集。eSheep 的原 csproj 含未被源代码使用的 stdole COM 引用，需要缺失的 AxImp 工具；验证生成私有 csproj，仅移除此引用，保留所有上游逻辑。已跟踪上游文件保持不变。

## 实际验证结果

统一回执：`data/receipts/pets/e5cf56a2a5c94465a1197d50c86e13be/receipt.json`。包含上游提交、项目源码指纹、每一步真实退出码、可执行文件指纹、超时与进程退出状态。所有启动的测试进程已退出，源码运行期间未变。

| 能力 | VPet 实测 | eSheep 实测 | 改装含义 |
|---|---|---|---|
| 运动控制 | 原版 MoveTimer → IController.MoveWindows → 真实 WPF 窗口；移动与停止断言通过 | 真实窗口600ms段约38次位置更新；拖拽即时位置、附着窗口跟随通过；完整运行组随后因反射调用参数不匹配退出1 | VPet 公开控制契约更适合接入外部目标/多屏坐标适配；任意路线与避让仍需调度薄层 |
| 资源替换 | 原版 Picture 加载自有红蓝 PNG，经 Main.Display 在同一主体切换；渲染像素断言通过 | 原版 XML 解析器载入两套合成精灵表，创建新宠物发布正确帧；6项断言通过 | VPet 可从 IGraph/GraphCore 对接独立资源绑定；eSheep XML 同时包含外观与运动定义，需要适配分离 |
| 局部效果接口 | 在公开 UIGrid 添加黑色效果，渲染为不透明黑色；撤除不影响皮肤，断言通过 | 上游 TransitionRuntimeTests：两种平滑模式下边界、任务栏、窗口落地、序列转换产生实际 child；19条 PASS，退出0 | VPet 附加层适合锚点效果；身体窗口外的大范围效果仍需独立窗口层。eSheep child 适合精灵效果，复杂粒子需外部层 |
| 上游测试风险 | 本项目6项核心集成断言通过，原版 Core 编译0错误/警告 | MotionAuditTests 9项中3项失败；RuntimeMotionTests 参数异常；总验证脚本退出1 | 保留失败，不将部分通过写成整套成功；未将测试/源码不一致直接定性为所有发行版本的缺陷 |

VPet 像素断言来自测试控件离屏 RenderTargetBitmap；eSheep 读取测试 PictureBox 发布的合成帧。两者均不截取或持久化用户桌面。VPet 资源替换试验使用静态 Picture，完整 PNG/APNG 动画、骨架、状态绑定和用户皮肤包矩阵仍待验证。

## 主体组合与模块边界

- **M09 宿主**：小型 WPF 宿主引用 VPet Core，复用其表现/互动核心。设置、暂停、数据根与模块生命周期归项目；完整上游 Windows 产品不作为必须捆绑的主体。
- **M04 运动**：通过 IController 适配真实窗口坐标、DPI 与显示器；复用现有动画/运动计时器，增加单一行为仲裁和外部路径目标。首次集成先保持一个位置控制者，避免自主移动与覆盖目标竞争。
- **M06 资源**：资源包通过 IGraph/GraphCore 绑定；皮肤、动画语义和局部特效分别引用，优先读现成格式。当前自有合成测试图形不承担正式角色资源。
- **M05 效果**：UIGrid 承担身体锚点效果，独立可穿透窗口承担跨身体烟雾/黑幕。粒子需求成立时接 Effekseer 等成熟运行库；实际 alpha/形状/时长回执由适配器提供。
- **M01 感知**：独立复用 windows-capture Rust 纹理接口。生产部署选择同进程桥接或受控本地服务时，分别验证设备/纹理共享、同步和跨显卡成本。
- **M02/M03**：开发产品独有的静态观察、自遮挡处理、覆盖账本、避让与有序随机调度；不重建桌宠引擎或 WGC。

## 验证范围与下一闭环

主体选择与关键接口接通证据保持；后续定向移动、独立黑幕、焦点/穿透和异常撤除已在 HOST-INTEGRATION 记录。完整 C04/C05/C06/C09 与 G1 仍未关闭；实际仅一块物理屏，多屏/混合DPI、完整互动、正式资源包替换矩阵及长稳继续验证。

选定主体上的可替换资源、定向移动、局部黑幕与窗口观测回执切片已完成，下一步接静态识别/覆盖调度，执行权威以 PLAN 为准。视觉/API/任务接口沿用既定契约。依赖完整锁定、分发素材及内置资源声明在集成/发布门集中核验。

本记录随主体集成里程碑维护；历史选型由 Git 保留，原始失败与回执至少保留至对应模块门禁关闭。

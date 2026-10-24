# VPet 工作宿主

归属 AI-pet；复用固定检查点 VPet Core（见 docs/HOST-SELECTION），实现项目特有的目标控制、资源绑定与局部效果接口，源码随 Git 保留。缓存、运行输入、日志、回执与构建产物归项目 data。

`Contracts.cs` 定义物理像素、显示身份/epoch、定向请求、局部效果及覆盖回执。`Native.cs` 为被动窗口的 Win32 薄适配。`VPetActor.cs` 使用 VPet Main/Picture/MoveTimer，通过 IController 到达目标；皮肤提供语义姿态资源，动画提供姿态/时长序列，效果保持独立。当前宿主执行单显示范围计划，稳定显示身份由未来 M01 拓扑适配提供。

项目私有 SDK 与固定上游位于 data/toolchains、data/upstream。`Directory.Build.props` 将中间输出集中至 data/build。真实窗口测试在 tests/pet-hosts/host-integration.csproj，程序与资源接口按 docs/CONTRACTS 语义逐步固化。该目录按模块验收与后续引用维护，退役前保留必要回归和证据。

## 执行与分层

- `VPetActor` 接受独立 `SkinDefinition`（语义姿态→已解析本地图形）与 `AnimationClip`（语义姿态/时长序列），由 VPet Picture/Main.Display 渲染。动画节拍不持有皮肤私有路径；皮肤切换保留动作序列。每角色图形缓存上限256项。
- `TargetController` 复用 VPet MoveTimer，按物理像素/秒到达目标；单一活动目标，拒绝并行替换。取消、截止和 dispose 都返回实际位置。只启用当前需要的核心接口，自主行为和完整互动由后续仲裁切片接入。
- `EffectSession` 通过私有标准输入管道启动 `BlackoutWorker`，使用随机nonce核对就绪；效果不读取角色资源。黑幕有最大640×640面积、最长5秒、宿主心跳900ms与独立硬截止。stdin/进程存活负责父子生命周期，不监听网络端口。
- `Native` 设置 NOACTIVATE、TOOLWINDOW，黑幕另设 LAYERED/TRANSPARENT，使用物理坐标 SetWindowPos 与 NOACTIVATE，并处理鼠标命中消息。效果为局部不透明纯黑，未使用黑色色键。
- `HostRun` 接受一个不超过64KiB的已解析 JSON 计划，单次运行一角色、一移动目标、一效果。输入指向受信任的已解析资源；包导入安全检查归 M06，当前入口不承担远程API或自动资源安装。

```powershell
./tools/host/verify.ps1
# 上述工具生成 plan.json 后，可用新的 data 输出目录单独执行：
./data/toolchains/dotnet/dotnet.exe ./data/build/host-integration/AIpet.Host.dll --run-plan <plan.json> <fresh-data-output-directory>
```

构建要求固定上游和项目私有SDK已就绪；verify 会核验上游检查点、按警告作为错误构建、执行真实窗口测试和完整计划，再保存源指纹/退出码回执。`--owner-test*`、`--overlay-test-hang` 是开发故障注入入口，正常计划不暴露该能力。

覆盖回执的 `ObservedMs` 来自窗口可见/几何检查及DWM提交后的单调时间，`WindowPixelMs` 为实际窗口面积×观测时长。它明确标记外部遮挡未知，不能直接解释成全部像素均实际调暗或面板寿命收益。没有回执的崩溃返回零信用；卡死后未经观察的时间不累计。父进程独立核对原窗口已消失，补充撤除确认与工作进程实际退出码。

真实多屏、混合DPI、热插拔、透明度渐变、身体形状账本、其他置顶窗口遮挡、完整输入行为矩阵和8小时长稳仍待验证。M04/M05/M06/M09完整验收与G1集中审核保持未关闭，基础宿主证据见 docs/HOST-INTEGRATION.md，闭环证据见 docs/CLOSED-LOOP.md。

## 静态识别与覆盖调度闭环

`GpuObserver` 复用Rust统计流，只保留最新健康快照；600ms未更新或源退出即视为不可派发。`StaticMap` 将细分块投影至64×64区域，保留稳定亮边缘的观测时长和自身遮挡未知状态。StableMs表示区域内受关注稳定内容的时长特征，不表示整块所有像素都静止。

`CoverageScheduler` 在可观测、静止≥500ms的候选中按最少实际窗口服务时间选择，固定种子打散平手；回执按planId去重、核验显示epoch/时间/面积，缺失证据零信用。公平性账本衡量观测窗口服务，不冒充已校准的OLED调暗剂量。

`ClosedLoop` 将GPU统计→区域候选→VPet目标→独立黑幕→回执账本连通。运动前预约完整扫过路径的保守遮罩并等待GPU代次确认，到位后等待实际身体遮罩确认；目标效果遮罩保持到撤除。源失效或操作区相交会取消。`Native.ReadOperationContext` 只读鼠标位置、最近输入时间和可用光标矩形，拒绝近期输入和受保护路线/目标，不读取键入正文。

外部入口：`AIpet.Host --closed-loop <resolved-loop-plan.json> <observe.exe> <fresh-data-output-directory>`。运行限1—12秒、最多8个动作，当前单主屏有界ROI。完整验证在 tools/loop/verify.ps1，结果边界见 docs/CLOSED-LOOP.md。

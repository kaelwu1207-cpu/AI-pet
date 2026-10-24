# 桌宠主体接口验证

AI-pet 维护，可运行测试定义随 Git 保留；第三方源码、私有依赖、合成图形与运行日志留在 data。选型和固定提交见 docs/HOST-SELECTION.md。

- `VPetProbe.cs` / `vpet-probe.csproj`：实际引用固定检查点 VPet 核心，运行其 MoveTimer，通过 IController 移动真实 WPF 窗口；使用原版 Picture/Main.Display 替换自有红蓝 PNG，并通过 UIGrid 添加、渲染、撤除黑色效果。像素断言来自测试控件离屏渲染，不读取用户桌面。
- `ESheepResourceProbe.cs`：编译引用 eSheep Portable 程序，原版 XML 解析器加载两套合成精灵表，验证真实宠物发布的帧。XML 重新加载创建新宠物实例，动画入口通过反射；这是需要薄层改造的证据。
- `Directory.Build.props`：将验证程序中间输出集中到 data/build，避免与上游项目资产文件冲突。
- `host-integration.csproj` / `HostIntegration.cs`：引用实际宿主，验证物理目标、取消、超时、资源解耦、被动窗口和局部效果生命周期；失败导致非零退出。
- `probe_blackout.py`：通过已安装 windows-capture 从WGC帧内只检查自有测试窗口的黑幕内部与外围ROI，输出布尔结果、不保存画面。该测试使用CPU映射验证合成显示，生产采集仍走Rust原始纹理；当前限定单物理屏。

执行入口为 tools/pet-hosts/verify.ps1。上游运动/子效果测试也被调用，失败保留真实退出码。单屏、合成资源、反射设置和人工鼠标体验的验证边界分别记录；不将局部接口通过视为完整主体或多屏验收。退役时先保留选择依据与必要回归。

当前宿主整合使用 tools/host/verify.ps1，单独保留上游对照测试结果。跨进程 WindowFromPoint/WM_NCHITTEST 验证输入穿透，不移动用户鼠标或注入真实键鼠输入；原始前台句柄用于核对不抢焦点。故障覆盖宿主异常/被杀、心跳丢失、效果UI卡死和效果进程无回执退出。

`LoopLogic.cs` / `loop-logic.csproj` 验证静态事实冻结、测量回执一致性/去重、公平分配和操作禁入。`LoopGpu.cs` / `loop-gpu.csproj` 在真实窗口生成1物理像素细线、闪烁文本光标和动态色块，通过Rust GPU统计验证时长，再执行VPet/黑幕/回执闭环。操作测试注入策略区域，保留真实鼠标/caret空间禁入，受控回放近期输入位；纯逻辑另测近期输入暂停。外部JSON入口使用完整原生操作策略。执行及回执入口为 tools/loop/verify.ps1；输入引起的暂停/取消保留实际原因。测试不注入系统键鼠事件。
`FullScreen.cs` / `full-screen.csproj` 测量真实全屏统计与宿主聚合/调度延迟，并以同等实际服务时长回执验证全屏网格公平性。原始结果进入data/receipts/performance；只保存性能与计数。GPU时间由D3D时间戳查询取得，CPU按单核等效百分比表达，显存计数与自有资源字节分别记录。工具及夹具随模块维护。
`TopologyTests.cs` 验证枚举/缩放/断开/重连/负坐标/epoch和ROI重定位。`RecoveryTests.cs` 使用实际WGC统计进程与自有黑幕，验证主动重开、旧拓扑启动拒绝、历史重置、旧回执拒绝及启动几何竞态；断开/重连由受控快照注入，真实物理插拔另行验收。定义由loop-logic/loop-gpu项目维护，结果进入data/receipts/loop。

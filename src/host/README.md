# VPet 工作宿主

归属 AI-pet；复用固定检查点 VPet Core（见 docs/HOST-SELECTION），实现项目特有的目标控制、资源绑定与局部效果接口，源码随 Git 保留。缓存、运行输入、日志、回执与构建产物归项目 data。

`Contracts.cs` 定义物理像素、显示身份/epoch、定向请求、局部效果及覆盖回执。`Native.cs` 为被动窗口的 Win32 薄适配。`VPetActor.cs` 使用 VPet Main/Picture/MoveTimer，通过 IController 到达目标；皮肤提供语义姿态资源，动画提供姿态/时长序列，效果保持独立。当前宿主执行单显示范围计划，稳定显示身份由未来 M01 拓扑适配提供。

项目私有 SDK 与固定上游位于 data/toolchains、data/upstream。`Directory.Build.props` 将中间输出集中至 data/build。真实窗口测试在 tests/pet-hosts/host-integration.csproj，程序与资源接口按 docs/CONTRACTS 语义逐步固化。该目录按模块验收与后续引用维护，退役前保留必要回归和证据。

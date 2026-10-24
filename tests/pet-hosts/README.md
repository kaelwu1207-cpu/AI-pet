# 桌宠主体接口验证

AI-pet 维护，可运行测试定义随 Git 保留；第三方源码、私有依赖、合成图形与运行日志留在 data。选型和固定提交见 docs/HOST-SELECTION.md。

- `VPetProbe.cs` / `vpet-probe.csproj`：实际引用固定检查点 VPet 核心，运行其 MoveTimer，通过 IController 移动真实 WPF 窗口；使用原版 Picture/Main.Display 替换自有红蓝 PNG，并通过 UIGrid 添加、渲染、撤除黑色效果。像素断言来自测试控件离屏渲染，不读取用户桌面。
- `ESheepResourceProbe.cs`：编译引用 eSheep Portable 程序，原版 XML 解析器加载两套合成精灵表，验证真实宠物发布的帧。XML 重新加载创建新宠物实例，动画入口通过反射；这是需要薄层改造的证据。
- `Directory.Build.props`：将验证程序中间输出集中到 data/build，避免与上游项目资产文件冲突。
- `host-integration.csproj` / `HostIntegration.cs`：引用实际宿主，验证物理目标、取消、超时、资源解耦、被动窗口和局部效果生命周期；失败导致非零退出。

执行入口为 tools/pet-hosts/verify.ps1。上游运动/子效果测试也被调用，失败保留真实退出码。单屏、合成资源、反射设置和人工鼠标体验的验证边界分别记录；不将局部接口通过视为完整主体或多屏验收。退役时先保留选择依据与必要回归。

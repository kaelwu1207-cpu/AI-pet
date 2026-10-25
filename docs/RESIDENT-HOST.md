# 常驻宿主与运行验证

AI-pet 的常驻宿主使用现有 VPet Core 渲染/运动、windows-capture 原始GPU纹理消费者、静态观察、覆盖调度和独立黑幕。宿主维护生命周期和资源组合，用户从系统托盘选择“暂停、继续、退出”。源码、开发输出和日用发布继续遵循 README 的根登记。

## 生命周期

`ResidentHost` 从 Paused 经 Starting 进入 Running；PauseAsync 先进入 Pausing，取消定向移动和当前黑幕，等待独立进程撤除回执及采集退出，再确认 Paused。暂停后宠物窗口关闭、动画停止。ResumeAsync 重新查询原先选定显示器，以新epoch创建角色、GPU历史与当代公平账本。ExitAsync 等待同样的撤除流程后进入 Stopped。转换由异步信号量串行化，重复暂停/退出可安全调用。

同一次连续运行保留覆盖服务账本；最近4096回执ID有界去重，淘汰记录的时间水位拒绝过期回放。最近128动作及当前区域服务汇总保留在内存，完整动作记录按4MiB轮转。每代次输出一份结束结果；单次宿主最多128代次，达到后进入可诊断故障状态。捕获连续失败3次进入 Faulted，可从托盘恢复；缺少选定显示器进入 WaitingDisplay，等待同一设备，期间关闭旧角色/采集。

Rust observe 的 `Resident=true` 增加租约模式，宿主UI调度每400ms续租；2秒无续租、stdin关闭、拓扑改变或停止命令结束原采集。原有有界模式继续采用1—30秒限制。独立黑幕沿用既有父身份、100ms心跳、900ms失联撤除、效果硬截止与进程兜底。窗口保持 NOACTIVATE、透明命中与跨进程鼠标穿透，托盘菜单接受用户主动点击。

默认每次动作后至少间隔5秒。全屏任务选择目标旁的可用施法位置，运动截止按距离计算；运动与效果继续使用保守自遮罩、600ms统计新鲜度、原生近期输入/鼠标/光标避让。暂停取消不能将计划覆盖算成实际覆盖。真实双屏与物理插拔、HDR验收分别执行。

## 资源组合

内置 green/pink 两套 Kenney New Platformer Pack 1.1 原始角色帧（CC0），rest/wander 两套独立时序（项目 Apache-2.0）。通过共同 `kenney-platformer/1` 帧角色绑定，播放复用 VPet Picture，位移复用 VPet MoveTimer，黑幕继续独立进程。来源、许可、归档与逐帧指纹见 assets/README.md 和各 manifest。

`AssetPacks.Load(root,skinId,motionId)` 支持本地已展开目录：清单最大64KiB，每皮肤1—64姿态，每动作1—128步，50—5000ms/步，PNG单图≤512×512、4MiB，每皮肤≤16MiB。加载验证版本、兼容rig、许可存在、大小和SHA-256，拒绝路径穿越、绝对路径、链接逃逸、未知字段和执行入口。解析成功再交给角色；不兼容组合保持原组合。当前2×2皮肤/动作矩阵已建立，第二种复杂特效及跨rig矩阵由M06后续推进。

## 入口与统计

构建至 `data/build/resident` 后：

```text
data/toolchains/dotnet/dotnet.exe data/build/resident/AIpet.Host.dll --resident <config.json> <observe.exe> <fresh-data-output>
```

配置字段 `AssetsRoot`、`Skin`、`Motion`、可选稳定 `DisplayId` 与 `ActionIntervalMs`；省略显示身份时首次锁定主屏，随后按稳定ID等待。开发启动器通过重定向stdin发送 pause/resume/status/exit；通道EOF使宿主退出。日常可见控制为托盘。

`tools/resident/run.ps1` 启动同一正式常驻入口进行15秒—8小时有界试验，记录检查点、逐文件指纹、实际退出码、控制事件和时长。每秒统计宿主、采集的CPU/私有内存/工作集/句柄与GPU统计开销；独立黑幕记录100ms采样内存峰值及结束CPU。退出汇总CPU包含全部完成采集代次与黑幕子进程；内存为采样值，分量峰值之和是保守上界，并非同一时刻实测峰值。

metrics按8MiB双文件轮转；结束回执只输出有界状态与统计，不含屏幕像素。CPU机器占用按实际逻辑处理器数归一；GPU查询只覆盖ROI复制和计算统计，不代表整卡利用率。长稳验收分别核实真实运行时长、所有进程退出、故障/采集重开次数、内存/句柄趋势、焦点与穿透；启动试验与试验通过分别登记。

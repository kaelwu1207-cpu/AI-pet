# AI-pet 文档索引与维护

本目录由 AI-pet 项目维护，保存用户授权的产品与开发定义，来源为 2026-10-24 至 2026-10-25 的需求讨论、开源调研及用户在 2026-10-25 的固化指令。

## 当前文档角色

| 顺序 | 文件 | 当前角色 |
|---|---|---|
| 1 | [PLAN.md](PLAN.md) | 唯一模块主计划、执行工作包、进度和门禁权威 |
| 2 | [PRODUCT.md](PRODUCT.md) | 当前产品目标、需求与交付范围 |
| 3 | [ARCHITECTURE.md](ARCHITECTURE.md) | 当前技术架构与隔离运行边界 |
| 4 | [CONTRACTS.md](CONTRACTS.md) | 模块通信、数据所有权与兼容语义 |
| 5 | [ASSET-PACKS.md](ASSET-PACKS.md) | 皮肤、动画和特效独立替换规范 |
| 6 | [OPEN-SOURCE.md](OPEN-SOURCE.md) | 调研证据、候选及复用决策 |
| 7 | [CAPTURE-STATUS.md](CAPTURE-STATUS.md) | 当前模块实施与验证证据、已知缺口 |
| 8 | [HOST-SELECTION.md](HOST-SELECTION.md) | VPet/eSheep 固定源码实测、主体选择和适配边界 |
| 9 | [HOST-INTEGRATION.md](HOST-INTEGRATION.md) | 定向运动、独立黑幕、覆盖回执与焦点/穿透/故障撤除实测 |
| 10 | [CLOSED-LOOP.md](CLOSED-LOOP.md) | GPU静态统计、自身遮挡、公平调度、操作避让与反馈闭环 |
| 11 | [OBSERVATION-BASELINE.md](OBSERVATION-BASELINE.md) | 共享冻结阈值、GPU真值夹具、全屏性能与参考预算 |
| 12 | [DISPLAY-LIFECYCLE.md](DISPLAY-LIFECYCLE.md) | 设备接口身份、物理坐标、拓扑代次与有界重开 |

采集、宿主、闭环的执行证据分别由 CAPTURE-STATUS、HOST-INTEGRATION、CLOSED-LOOP 维护，原始回执位于 data。当前无辅助主计划、顾问审查结论或被替代主计划。当前计划角色始终排在历史记录之前；不把屏幕帧、密钥或运行日志写进 docs。

## 维护与保留

保持文件名和模块/需求/接口 ID 稳定；当前有效规格就地更新并通过本地 Git 保留历史。需求变化同步更新需求映射、接口、工作包和验收，不另起竞争主计划。只有出现独立读者或明确生命周期差异时才拆文档。重大版本确需归档时登记替代关系、保留原因和复核事件。

本目录覆盖当前产品规格、架构、资源包规范、研究记录和计划类文件。验证回执采用运行身份与检查点引用，不能用勾选或叙述替代实际证据。文档保留至项目退役与所需历史保存完成。
`RESIDENT-HOST.md` 维护常驻生命周期、资源包准入、托盘/开发控制接口及完整运行计量；随M06/M09/M10变更更新，证据保留于data。


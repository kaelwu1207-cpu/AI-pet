# AI-pet：工作期间的局部动态屏保宠物

AI-pet 是 Windows 桌面宠物：在持续工作期间，通过飞行、攀爬、玩耍与局部魔法，间歇改变、遮挡或调暗长期静止的画面。后台根据静态暴露、实际覆盖时长和操作避让安排活动，前台保持自然、有性格的表现。

当前阶段：**独立 Git 已建立，已直接接入 windows-capture 2.0.1 并通过当前单屏验证；多屏和完整产品待验收。** Git 元数据位于本目录 `.git`，初始文档提交为 `7c6c3bc05949f053abf40f7de952729371d4f5e4`。当前 Windows 仅枚举到一块活动显示器。详见 [采集实施记录](docs/CAPTURE-STATUS.md)。

## 文档入口与权威顺序

| 文档 | 角色与用途 |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | 当前唯一模块主计划；模块、工作包、进度、门禁与续作入口 |
| [docs/PRODUCT.md](docs/PRODUCT.md) | 产品定位、当前交付范围、需求与产品验收 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 技术架构、数据流、平台与隔离执行边界 |
| [docs/CONTRACTS.md](docs/CONTRACTS.md) | 模块接口、数据所有权、状态与兼容规则 |
| [docs/ASSET-PACKS.md](docs/ASSET-PACKS.md) | 可替换的皮肤、动作动画、特效包与绑定契约 |
| [docs/OPEN-SOURCE.md](docs/OPEN-SOURCE.md) | 开源候选、已读源码依据、复用边界与验证缺口 |
| [docs/README.md](docs/README.md) | 文档职责、维护与保留规则 |
| [docs/CAPTURE-STATUS.md](docs/CAPTURE-STATUS.md) | 当前采集实现、真实验证、缺口和下一步 |
| [THIRD-PARTY.md](THIRD-PARTY.md) | 已采用组件及版本；`requirements-capture.txt` 锁定验证环境依赖 |

用户最新明确指令与全局/上级目录规则优先。产品目标由 PRODUCT 维护；计划执行由 PLAN 维护；接口和资源包分别由 CONTRACTS、ASSET-PACKS 维护。冲突应显式修订相关文档后继续，文件名和修改时间不改变权威角色。

## 项目根与数据角色登记

所有路径均归 AI-pet 所有；下表中“规划”不是创建、迁移、运行切换或发布证据。

| 角色 | 绝对路径 | 实际状态与生命周期 |
|---|---|---|
| 开发源码与程序定义 | `D:\ai\projects\AI-pet` | 已存在；独立本地 Git 持久维护 |
| 开发数据根 | `D:\ai\projects\AI-pet\data` | 已建立；[角色与生命周期](data/README.md)，排除于 Git 和程序包 |
| Hyper-V 数据 | `D:\ai\projects\AI-pet\data\hyperv` | 规划；子盘、配置、受保护控制凭据、输入和生命周期记录；每次运行按登记完成事件处置私有状态 |
| 一致输入与资源试验输入 | `D:\ai\projects\AI-pet\data\inputs` | 规划；只读快照和已许可外部输入；按来源及对应运行到期规则保留 |
| 有界工程回执 | `D:\ai\projects\AI-pet\data\receipts` | 已建立采集回执；保留状态、指标、错误与校验；至少保留到相关集中门禁关闭，随后按回执保留要求复核 |
| 开发模型/资源/依赖缓存 | `D:\ai\projects\AI-pet\data\cache` | 已建立依赖缓存；按依赖引用及空间预算清理；不进入源码包 |
| 开发配置与凭据 | `D:\ai\projects\AI-pet\data\config` | 规划；凭据保护存储，日志脱敏；到期或替换后撤销引用 |
| 开发临时/日志/备份 | `D:\ai\projects\AI-pet\data\temp`、`data\logs`、`data\backups` | temp/logs 已建立，backups 仍规划；临时材料随运行结束清理，诊断与恢复材料按 data README 保留 |
| 日常发布根 | `D:\ai\local-product-releases\AI-pet` | 规划，尚未创建或发布；只通过项目未来正式发布流程进入 |
| 日常运行数据根 | `D:\ai\local-product-releases\AI-pet\data` | 规划，尚无运行数据或根切换；维护实际日用配置、密钥、日志和状态 |
| E 盘业务材料 | 无 | 本项目没有登记或使用 E 盘业务内容根 |

新增持久开发角色先更新本登记及相邻 README。不得借助第三方默认路径在未授权位置落地项目模型、缓存或实例；系统管理的应用状态保持原归属。data README 记录环境、构建和回执等角色，实际数据排除 Git/打包。

## 未来目录布局

已建立 `src/capture`、`tools/capture`、`tests/capture` 和 `data`，分别由 [采集模块说明](src/capture/README.md)、[工具索引](tools/README.md)、[测试索引](tests/README.md)、[数据角色](data/README.md)覆盖。其余目录仍为规划：

- `src/host/`：桌面生命周期、设置与 Windows 桥接。
- `src/capture/`、`src/observation/`、`src/coverage/`：GPU 帧、多屏静态分析与覆盖调度。
- `src/pet/`、`src/effects/`、`src/assets/`：运动表现、局部效果和资源包加载。
- `src/vision/`、`src/integrations/`：视觉模型与 API/任务监控适配器。
- `contracts/`：接口和资源包的机器可校验 schema、兼容夹具。
- `assets/`：具有明确授权、可随程序分发的自有或第三方资源；用户导入包位于相应数据根。
- `tests/`：针对契约、算法、渲染和整合行为的测试定义；真实画面测试输入归数据输入角色。
- `tools/hyperv/`：可选隔离控制定义，当前不存在；本项目已获宿主开发豁免。
- `data/`：已建立的集中开发数据，含私有依赖环境、缓存、构建与回执。

源码、接口、测试定义和许可明确的分发资源随模块持久维护；淘汰前检查引用与历史保留。第三方代码按 OPEN-SOURCE 中的选定提交、许可证、改造记录和验收结果纳入，允许跨项目拼接能力。

## 开发执行与发布状态

用户于 2026-10-25 明确豁免本项目的 Hyper-V 开发流程，详见 [项目规则](AGENTS.md)。宿主可执行开发、构建、测试与真实 GPU 采集；Hyper-V 为可选路径。开发数据与日用发布边界保持。

当前没有项目客体或 VM；宿主源码检查点与实际验证结果见采集实施记录。未来选择 Hyper-V 时，消费共享父盘前须阅读其 README 与当前生命周期证据，核验密封状态、代次、只读属性、SHA-256 和有效期限。

下一步优先接入上游 Rust GPU 纹理接口并补齐持久显示身份/恢复；第二屏连接后执行实际多屏验收。生产采集优先复用开源引擎，不扩写自有 WGC 后端。日常发布尚未进行。

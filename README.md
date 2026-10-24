# AI-pet：工作期间的局部动态屏保宠物

AI-pet 是 Windows 桌面宠物：在持续工作期间，通过飞行、攀爬、玩耍与局部魔法，间歇改变、遮挡或调暗长期静止的画面。后台根据静态暴露、实际覆盖时长和操作避让安排活动，前台保持自然、有性格的表现。

当前阶段：**产品与模块计划基线已建立，程序尚未实现，工程与产品验收尚未执行。** 本目录在 2026-10-25 建档前为空，尚未初始化独立 Git 仓库。文档完成不代表功能完成。

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

用户最新明确指令与全局/上级目录规则优先。产品目标由 PRODUCT 维护；计划执行由 PLAN 维护；接口和资源包分别由 CONTRACTS、ASSET-PACKS 维护。冲突应显式修订相关文档后继续，文件名和修改时间不改变权威角色。

## 项目根与数据角色登记

所有路径均归 AI-pet 所有；下表中“规划”不是创建、迁移、运行切换或发布证据。

| 角色 | 绝对路径 | 实际状态与生命周期 |
|---|---|---|
| 开发源码与程序定义 | `D:\ai\projects\AI-pet` | 已存在；持久维护，后续建立独立本地 Git 历史 |
| 开发数据根 | `D:\ai\projects\AI-pet\data` | 规划；实际数据、密钥、实例、缓存、日志、临时材料均归此角色，排除于 Git 和程序包 |
| Hyper-V 数据 | `D:\ai\projects\AI-pet\data\hyperv` | 规划；子盘、配置、受保护控制凭据、输入和生命周期记录；每次运行按登记完成事件处置私有状态 |
| 一致输入与资源试验输入 | `D:\ai\projects\AI-pet\data\inputs` | 规划；只读快照和已许可外部输入；按来源及对应运行到期规则保留 |
| 有界工程回执 | `D:\ai\projects\AI-pet\data\receipts` | 规划；只保留授权的状态、指标、错误、校验与清理回执；至少保留到相关集中门禁关闭，随后按回执保留要求复核 |
| 开发模型/资源/依赖缓存 | `D:\ai\projects\AI-pet\data\cache` | 规划；按依赖引用及空间预算清理；不进入源码包 |
| 开发配置与凭据 | `D:\ai\projects\AI-pet\data\config` | 规划；凭据保护存储，日志脱敏；到期或替换后撤销引用 |
| 开发临时/日志/备份 | `D:\ai\projects\AI-pet\data\temp`、`data\logs`、`data\backups` | 规划；临时材料随运行结束清理，诊断与恢复材料由创建记录规定到期日/事件 |
| 日常发布根 | `D:\ai\local-product-releases\AI-pet` | 规划，尚未创建或发布；只通过项目未来正式发布流程进入 |
| 日常运行数据根 | `D:\ai\local-product-releases\AI-pet\data` | 规划，尚无运行数据或根切换；维护实际日用配置、密钥、日志和状态 |
| E 盘业务材料 | 无 | 本项目没有登记或使用 E 盘业务内容根 |

新增持久开发角色先更新本登记及相邻 README。不得借助第三方默认路径在未授权位置落地项目模型、缓存或实例；系统管理的应用状态保持原归属。未来创建 `data` 后应建立其角色与生命周期 README，并配置 Git/打包排除。

## 未来目录布局

除当前 README 与 docs 文档外，以下均是预定布局：

- `src/host/`：桌面生命周期、设置与 Windows 桥接。
- `src/capture/`、`src/observation/`、`src/coverage/`：GPU 帧、多屏静态分析与覆盖调度。
- `src/pet/`、`src/effects/`、`src/assets/`：运动表现、局部效果和资源包加载。
- `src/vision/`、`src/integrations/`：视觉模型与 API/任务监控适配器。
- `contracts/`：接口和资源包的机器可校验 schema、兼容夹具。
- `assets/`：具有明确授权、可随程序分发的自有或第三方资源；用户导入包位于相应数据根。
- `tests/`：针对契约、算法、渲染和整合行为的测试定义；真实画面测试输入归数据输入角色。
- `tools/hyperv/`：项目隔离控制定义，拟定入口 `control.ps1`，当前不存在。
- `data/`：集中开发数据，当前不存在。

源码、接口、测试定义和许可明确的分发资源随模块持久维护；淘汰前检查引用与历史保留。第三方代码按 OPEN-SOURCE 中的选定提交、许可证、改造记录和验收结果纳入，允许跨项目拼接能力。

## 开发执行与发布状态

宿主允许编辑文档/源码、Git 操作、同步只读输入与可信原生 VM 控制。Python/Node 实验、开发预检、构建、程序运行与验证均在项目登记的 Hyper-V 客体执行；Windows Sandbox 路线已退役。

当前未登记运行中的项目客体、VM ID、源检查点或验证结果。共享系统父盘位于 `D:\ai\tools\hyperv\windows11-ltsc-eval\windows.vhdx`；消费前须阅读其 README 与当前生命周期证据，并核验密封状态、代次、只读属性、SHA-256 和有效期限。不得用目录登记替代核验。

下一步按 [PLAN 的开发就绪入口](docs/PLAN.md#12-开发就绪与下一步) 建立独立 Git 与项目隔离能力；本轮没有创建 VM、下载依赖、运行应用或修改日常发布目录。

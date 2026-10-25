# 开发工具定义

`pet-hosts/` 为主体选型的可复现构建/运行入口，固定源码、私有工具链与验证边界见 docs/HOST-SELECTION.md。`capture/build-rust.ps1` 构建并检查上游原始 GPU 纹理消费者，`capture/verify.ps1 -Backend rust` 保存真实采集回执。

`host/verify.ps1` 构建当前 VPet 改装宿主并执行定向移动、独立黑幕、资源解耦、焦点/命中测试、故障撤除和外部JSON计划。完整日志与回执集中于 data/receipts/host；实际屏幕条件和剩余门禁见 docs/HOST-INTEGRATION.md。工具随宿主模块维护，不启动日用安装版。

由 AI-pet 维护，当前 `capture/` 为采集模块开发控制：`build.ps1` 构建 Windows SDK 对照样例和单元测试，`probe_library.py` 验证直接安装的开源库，`verify.ps1` 执行有超时的真实捕获并保存身份/指纹/退出码回执。

工具定义随源码保留；产物、临时目录、下载和回执使用根 README / data README 的开发数据角色。当前用户已授权宿主开发。真实多屏/HDR/热插拔覆盖必须分别记录，不能由单屏结果代替。

`loop/verify.ps1` 构建GPU统计与宿主、运行静态/公平/避让逻辑测试、真实GPU图形测试及完整反馈闭环；原始输出、输入与指纹回执进入 data/receipts/loop。该工具随M02/M03维护，真实窗口测试采用独立自有ROI，不持久化像素。真实GPU夹具须在与WGC捕获相同的交互桌面执行；受限控制环境未显示的窗口不构成图像真值。构建采用单节点且不复用后台编译服务，结果边界见 docs/CLOSED-LOOP.md。
`loop/performance.ps1` 对冻结参考机执行1—3轮真实全屏GPU统计/宿主处理/公平调度测量，按contracts/performance-v1.json校验预算。每轮6秒测量加1秒预热，只保存计数、资源峰值及源码身份，结果归data/receipts/performance。其他硬件先登记同口径实测再建立独立预算。
`resident/`：真实常驻托盘宿主的有界性能及长稳控制，运行身份、输入、源码指纹与统计回执归项目 data；默认10分钟，8小时试验可单独启动。


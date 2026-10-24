# M01 采集模块实施与验证

日期：2026-10-25。当前执行方向：优先直接用/改开源，项目只维护必要适配和独有逻辑。

## 当前事实

- 独立 Git：`D:\ai\projects\AI-pet\.git`；基线提交 `7c6c3bc05949f053abf40f7de952729371d4f5e4`。本地代理身份复用同机既有项目的 `Codex <codex@local.invalid>`。
- 用户明确豁免本项目 Hyper-V 开发过程，已写入项目 AGENTS。开发数据、发布与凭据边界不变；未建立 VM，未修改日用发布。
- 已安装并实际使用 `windows-capture 2.0.1`；Python 环境位于 `data/envs/capture`，依赖见 requirements-capture.txt / THIRD-PARTY。
- `src/capture/windows_capture_adapter.py` 为薄层：身份/epoch、帧序列、消费回调与停止。WGC 捕获、线程、原生缓冲均由上游承担。
- 原生 C++ WGC 代码是早期硬件对照样例，暂停扩写，不作为需长期自维护的生产后端。
- `src/capture-rust` 已实际接通上游 `as_raw_texture()` / `device()` / `device_context()`：回调内复制至自有 GPU 纹理并用 EVENT 查询确认完成，无屏幕像素 CPU 读回。依赖由 Cargo.lock 固定，构建与 Clippy 零警告通过。
- 新增 `observe` 统计消费者：同一上游纹理在GPU内计算逐像素静止历史和16×16细分块计数，主屏有界ROI只读回计数；已接入宿主调度和覆盖回执，详见 [CLOSED-LOOP](CLOSED-LOOP.md)。

## 实际验证与证据

| 项目 | 结果 | 覆盖与证据 |
|---|---|---|
| 原生帧跟踪测试 | 10项断言通过 | 时间戳重复/乱序、旧epoch、停止和源隔离；纯逻辑测试 |
| 开源库薄适配测试 | 4项 unittest 通过 | 双来源时钟隔离、回调失败、停止后迟到帧、非法目标；使用测试源，不代表双物理屏 |
| 开源库真实单屏 | 通过，退出0 | 5120×2160，约4秒39帧，无拒绝/错误，停止成功；`data/receipts/capture/b9c2661e9b6549d0ac8bbec764233a8c/receipt.json` |
| 开源库真实多屏要求 | 条件不足，退出2 | 实际仅1块活动显示器，最低要求2；已有1路正常采集；`data/receipts/capture/2dca94caeae84165be3d5c62107613d1/receipt.json` |
| Rust 原始 GPU 纹理单屏 | 通过，退出0 | 5120×2160、格式10，4秒39帧，39次GPU复制均完成，无采集错误，无像素读回；`data/receipts/capture/1d6e0af443a1427ebd4d988d60ad2fc1/receipt.json` |
| Rust 真实双屏要求 | 条件不足，程序退出2 | 实际1屏，39帧正常，要求至少2屏故不通过；`data/receipts/capture/8402558e1ca149c88b1c51e3dec5b194/receipt.json`；外层终端返回1，回执保留实际子进程退出2 |
| 原生硬件对照 | 单屏通过 | RTX5090、5120×2160、DPI144；3秒97帧，受控ROI变化25次，FP16格式10；未保存画面；首轮工具结果记录，尚非源码检查点完整绑定的验收回执 |

回执包含 runId/nonce、Git 检查点与未提交源文件哈希、进程退出/超时、输入指纹、原始完整stdout/stderr。实际程序结果与源码/平台能力范围分别解释。当前无图片/视频持久化和云上传。

受限工具桌面的原生测试色块未反映到物理桌面采集ROI，切换到授权的交互桌面执行同一程序后变化检测通过；因此后续真实采集验证需要在正确交互桌面执行。该结果不是必须以管理员权限运行产品的证据。

## 当前限制

1. 只有一块活动物理屏，真实多屏、混合DPI、跨显卡、插拔/休眠、旋转均待验证。
2. Python公开接口映射GPU帧至CPU；Rust统计已覆盖5120×2160全屏，64px输出只读回计数，推理共享和长期生产服务生命周期待完成。阈值、夹具及资源结果见 OBSERVATION-BASELINE。
3. Rust消费者已通过设备接口路径解析指定显示器，帧绑定身份/epoch，拓扑变化和采集错误可有界重开。当前身份稳定范围、克隆源限制和真实硬件缺口见 DISPLAY-LIFECYCLE；旧Python桥仍为session-only。
4. 原生样例使用FP16不等于实际HDR显示验收；HDR状态、色彩与映射待验证。
5. 采集指示边框暂保持系统默认；其常驻亮区与无边框权限以后集中解决。
6. 完整 C01/G0/G2 未通过，当前完成上游Python采集与Rust原始GPU纹理消费切片。

## 下一步

扩充实际显示插拔/休眠、长期资源和工作场景矩阵。第二显示器可用后运行 `tools/capture/verify.ps1 -Backend rust -MinimumDisplays 2`，再验证指定设备身份的多路统计与恢复。VPet Core闭环入口为 `tools/loop/verify.ps1`，资源入口为 `tools/loop/performance.ps1`；当前边界见 CLOSED-LOOP / OBSERVATION-BASELINE / DISPLAY-LIFECYCLE。

本记录随模块里程碑更新，原始运行证据按 data README 保留；研究与验收不能互相替代。

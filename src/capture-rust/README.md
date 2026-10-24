# 上游原始 GPU 纹理验证

归属 AI-pet，维护 windows-capture 2.0.1 的薄消费者，不实现捕获后端。Cargo.lock 固定 Rust 依赖。工具链为项目 data 下 Rust 1.99.1，系统 MSVC 仅作链接器使用。

每个活动显示器建立独立上游会话；在回调借用期通过 `as_raw_texture()` / `device()` / `device_context()` 把帧复制到一张自有 DEFAULT 纹理，EVENT 查询确认 GPU 完成。查询只返回完成标志，未调用像素 Map、帧 buffer、编码或保存接口。格式使用 RGBA16F；格式支持与实际 HDR 显示验收分开。

每屏最多保留一张自有复制纹理，尺寸/格式变化时替换；GPU 查询有两秒上限。主程序采集四秒后停止并 join 所有成功启动的会话，输出 JSON 统计。最少显示器数不足返回2，运行失败返回1。外层工具提供进程超时。

```powershell
./tools/capture/build-rust.ps1
./tools/capture/verify.ps1 -Backend rust -MinimumDisplays 1
./tools/capture/verify.ps1 -Backend rust -MinimumDisplays 2
```

当前已验证单物理屏 GPU 复制和下述静态统计。稳定身份/拓扑代次、热插拔、多适配器与全屏性能预算仍待验收；源码随模块维护，产物/缓存/回执按 data README 保留。

## GPU静态统计流

`observe.exe` 复用同一个 windows-capture WGC 后端：原始纹理→GPU裁剪ROI→D3D11计算着色器→16×16分块计数。`gpu_analysis.rs` / `static_tiles.hlsl` 保留GPU上的逐像素RGB24/静止时长，仅将分块的可见/静态/稳定亮边缘/变化数量、最大已观测时长与亮度总和读回CPU。真实测试320×128 ROI每帧读回5120字节统计，未Map图像纹理。

stdin首行 `{"Roi":{"X":0,"Y":0,"Width":320,"Height":128}}`，后续 `{"Revision":1,"Masks":[...]}` 更新最多16个自身遮挡矩形，`stop`结束。stdout逐帧JSON含序列、源时间、MaskRevision和计数；宿主持续消费并只保留最新快照。输入EOF与15秒运行上限结束会话。ROI预算由冻结基线定义，当前最多16777216像素；物理全屏性能须单独记录。

帧间隔超过1000ms会重新建立静止历史；自身遮挡期间停止计时，不用黑幕色彩覆盖底层历史。首次揭露像素重新建立连续性，宿主另保留此前已观察的事实。颜色比较为8位捕获域的阈值特征；HDR处理仍待验证。原始GPU复制probe与统计流分别提供证据，未重写捕获后端。

`baseline.rs` 编译包含 `contracts/static-v1.json`，宿主读取同一嵌入资源。`gpu_tests.rs` 用自有合成纹理验证500ms边界、7/8色差、亮度/对比阈值、细线、闪烁、自遮挡、揭露重建、时长上限及非整块边界；测试只上传自有像素，读回计数。定义及夹具随Git维护，变更更新基线版本和验证证据。
`topology.rs` 用Windows QueryDisplayConfig / DisplayConfigGetDeviceInfo解析设备接口路径，并映射到上游Monitor；隐藏被动元数据窗口通过GetDpiForWindow取得精确DPI并立即销毁。--topology输出只读布局，统计入口可选DisplayId/TopologyEpoch/ExpectedSignature，支持负桌面坐标到纹理坐标转换。每500ms核对拓扑，变化或失联以退出3停止，私有restart控制以退出4重开；WGC生命周期继续由windows-capture提供。设备接口路径的稳定范围和恢复验证见docs/DISPLAY-LIFECYCLE.md。

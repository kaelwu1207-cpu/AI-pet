# 上游原始 GPU 纹理验证

归属 AI-pet，维护 windows-capture 2.0.1 的薄消费者，不实现捕获后端。Cargo.lock 固定 Rust 依赖。工具链为项目 data 下 Rust 1.99.1，系统 MSVC 仅作链接器使用。

每个活动显示器建立独立上游会话；在回调借用期通过 `as_raw_texture()` / `device()` / `device_context()` 把帧复制到一张自有 DEFAULT 纹理，EVENT 查询确认 GPU 完成。查询只返回完成标志，未调用像素 Map、帧 buffer、编码或保存接口。格式使用 RGBA16F；格式支持与实际 HDR 显示验收分开。

每屏最多保留一张自有复制纹理，尺寸/格式变化时替换；GPU 查询有两秒上限。主程序采集四秒后停止并 join 所有成功启动的会话，输出 JSON 统计。最少显示器数不足返回2，运行失败返回1。外层工具提供进程超时。

```powershell
./tools/capture/build-rust.ps1
./tools/capture/verify.ps1 -Backend rust -MinimumDisplays 1
./tools/capture/verify.ps1 -Backend rust -MinimumDisplays 2
```

当前已验证单物理屏 GPU 复制完成。尚待接入稳定身份/拓扑代次、热插拔重建、静态统计 shader、多适配器策略和性能预算；本程序是原始纹理接口接通证据，不是完整采集服务验收。源码随模块维护，产物/缓存/回执按 data README 保留。

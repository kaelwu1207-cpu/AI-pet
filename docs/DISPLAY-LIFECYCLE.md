# 显示器身份与捕获拓扑恢复

日期：2026-10-25。归属M01/M03/M04/M05/M09/M10。WGC捕获和会话线程继续复用windows-capture 2.0.1；新增薄层仅负责Windows元数据、上游Monitor匹配、代次与产品资源撤除/重开。

## 身份和坐标

Rust `topology.rs` 通过QueryDisplayConfig取得活动路径，用DisplayConfigGetDeviceInfo分别查询source和target。`DisplayId` 使用规范化的monitorDevicePath设备接口路径，GDI名称只映射本次上游Monitor，不作为持久身份。当前稳定范围为Windows设备接口实例，显示器换接口、驱动重安装或设备实例重建可能产生新ID；不猜测不同路径是否属于同一实体面板。

快照记录物理桌面矩形、旋转、刷新率、DPI、适配器/target映射与高级色彩状态。元数据程序先启用Per-Monitor V2，短暂创建不可见且不激活的系统类窗口，用GetDpiForWindow取得对应显示器DPI，随后销毁。以完整排序快照比较拓扑，枚举顺序改变不产生虚假代次；几何、DPI、连接或路径映射改变使旧代次失效。

每路GpuObserver可指定DisplayId、TopologyEpoch和ExpectedSignature；启动时核验快照，帧带实际显示身份与代次。桌面ROI及自身遮罩在进入GPU前转换到所选Monitor的纹理坐标，输出统计回到物理桌面坐标，支持负原点。选择阶段明确拒绝无法独立区分的克隆source；元数据缺失会停止或等待，保持已选显示器身份。

## 生命周期与故障语义

统计进程每500ms核对拓扑。变化/失联后停止上游会话并退出3；私有restart控制停止当前会话并退出4；其他采集错误保留实际错误码。捕获尺寸/格式变化也会结束旧流。600ms过期撤销活动覆盖，持续停滞触发重开；黑幕工作进程另以40ms窗口/几何核对和独立期限保护撤除。

外部 `--closed-loop` 默认进入RecoveringLoop：

1. 解析并固定目标设备身份。
2. 生成本次epoch，启动有界观察/调度/角色/效果。
3. 捕获中断时取消运动和黑幕，确认撤除、关闭旧源和角色。
4. 同一显示器不可用时等待；可用后按新边界重定位全屏或局部ROI。
5. 重开上游会话，使用新epoch、像素历史和当前代次账本，恢复动作。

协调器最多3次采集尝试、总运行请求1—30秒，每段最多12秒，原生调用/启动/撤除仍有各自有界期限。启动阶段几何已变化会重新查询，零帧阶段没有覆盖信用。各epoch结果保存在epochs目录；recovery-result记录阶段/等待，兼容loop-result汇总动作，账本只汇总当前epoch，旧阶段证据独立保留。旧代次回执不能进入新账本。长期常驻由后续宿主服务包扩展预算与状态保留。

## 验证范围

纯逻辑覆盖枚举重排、负坐标、缩放变化、移除/重连、源重开、重复身份拒绝及ROI重定位。真实GPU集成覆盖重复原生枚举、旧签名启动拒绝、活动黑幕期间停止/重开真实WGC进程、首帧历史重置、旧回执拒绝和启动几何竞态。断开/重连及启动竞态使用受控快照，未修改所有者的实际显示配置。

当前只有一块活动物理显示器。真实双屏/混合DPI/跨显卡/旋转、拔插/休眠恢复、HDR色彩和长期常驻仍须硬件矩阵验收。500ms拓扑轮询与操作避让延迟分开计量，单次受控操作测量不能解释为物理热插拔的延迟保证。

## 上游和API依据

- [windows-capture Monitor API](https://docs.rs/windows-capture/2.0.1/windows_capture/monitor/struct.Monitor.html)：枚举、设备名和原始Monitor映射。
- [QueryDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig)：活动显示路径与查询竞态。
- [DISPLAYCONFIG_TARGET_DEVICE_NAME](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name)：target设备接口路径。
- [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)：所选显示器上Per-Monitor窗口的有效DPI。

原始元数据和设备路径只在本地data回执中保存；公开源码包含适配和测试定义。相关证据至少保留至M01/G2集中验收，角色索引由docs/README和主计划维护。

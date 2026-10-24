# 开发工具定义

`pet-hosts/` 为主体选型的可复现构建/运行入口，固定源码、私有工具链与验证边界见 docs/HOST-SELECTION.md。`capture/build-rust.ps1` 构建并检查上游原始 GPU 纹理消费者，`capture/verify.ps1 -Backend rust` 保存真实采集回执。

由 AI-pet 维护，当前 `capture/` 为采集模块开发控制：`build.ps1` 构建 Windows SDK 对照样例和单元测试，`probe_library.py` 验证直接安装的开源库，`verify.ps1` 执行有超时的真实捕获并保存身份/指纹/退出码回执。

工具定义随源码保留；产物、临时目录、下载和回执使用根 README / data README 的开发数据角色。当前用户已授权宿主开发。真实多屏/HDR/热插拔覆盖必须分别记录，不能由单屏结果代替。

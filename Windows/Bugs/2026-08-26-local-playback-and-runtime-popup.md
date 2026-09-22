# 2026-08-26 本地试听失败与运行时提权弹窗

## 症状

- “试听录音”把 WAV 交给 `Process.Start(... UseShellExecute = true)`，在当前宿主中显示为
  remote 文件并且用户无法播放。
- “启用按键测试”每次用 `Verb = runas` 启动十分钟 helper，造成 UAC、黑框、文件占用、
  多个残留会话和启动等待。
- ATVV 客户端收到 `0x08` 后立即开麦，没有输入法选择，也没有第二次按键关闭的状态。
- 最后一页只能统计三个按键，没有实体键映射或 Codex/Claude Code 动作。

## 最小根因

1. 回放依赖系统文件关联而不是应用内音频对象。
2. 管理员权限放在每次运行路径，而不是一次安装的系统服务边界。
3. ATVV 控制写入封装在客户端内部，没有“等待选择 / 开始 / 录音中 / 停止”的业务状态机。
4. HID 报告只用于测试计数，没有手势识别、绑定模型、持久化和受限动作执行器。

## 修复

- 使用 WPF `MediaPlayer` 直接加载经 RIFF/WAVE 校验的绝对本地文件。
- 将精确指纹校验、WUDFHost 注入和 HID 读取放入自动启动的 `SayAllHidBridge` 服务；
  普通应用只读 `C:\ProgramData\SayAll\HidBridge\events.jsonl`。
- 增加 Windows TSF 输入法枚举/激活、纯状态机和显式 ATVV open/close 方法。
- 增加真实 RC003MS 渲染图热区、Codex/Claude Code 预设、单击/双击/长按识别、JSON 设置，
  并把 SendInput 限制在 Codex、Claude 或已列入允许范围的终端前台进程。

## 验证证据

- `Windows\scripts\test-baseline.ps1` 全部通过且三个构建零警告。
- `Windows\scripts\publish-installer.ps1` 成功生成自包含安装包，并重新验证 Gadget SHA-256。
- 新按键配置页在 1120×760 真机桌面渲染检查中无溢出。
- 最终的听音、点按开始/停止、TSF 切换和 Codex/Claude Code 动作按
  `Windows\Testing\RC003MS_CONFIGURATION_TEST.md` 由用户验收。

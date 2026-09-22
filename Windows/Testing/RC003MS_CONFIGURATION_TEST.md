# RC003MS Windows 配置与真机验收

## 验收目标

证明 VoiceAnything 在这台 Windows 11 电脑上满足以下用户可见行为：日常启动无 UAC/CLI，
RC003MS 录音可在应用内听到，按住麦克风键时录音、松开后结束，输入法和实体键
映射会保存，且已启用的方向/确认键能在 Codex 或 Claude Code 前台执行动作。

## 准备

1. Windows 蓝牙页面显示“小米蓝牙语音遥控器”或“MI RC”已连接。
2. VB-CABLE 已安装，Windows 中存在 `CABLE Input (VB-Audio Virtual Cable)`。
3. 音箱或耳机可正常播放系统声音。
4. 关闭旧预览版；运行 `artifacts\VoiceAnything-Setup\VoiceAnything.Setup.exe` 并只批准这一次安装授权。
5. 安装后确认服务 `VoiceAnythingHidBridge` 状态为“正在运行”、启动类型为“自动”。

## A. 日常启动不弹窗

1. 关闭安装后自动打开的 VoiceAnything。
2. 从开始菜单打开 VoiceAnything。
3. 等待连接页显示“本地按键测试已就绪”，按一个方向键。

通过：没有 UAC、命令行黑框或独立 helper 窗口；界面显示真实按键名称。

失败：出现授权/黑框，或一分钟后仍显示“后台按键组件未安装”。查看
`C:\ProgramData\VoiceAnything\HidBridge\events.jsonl` 和
`C:\ProgramData\VoiceAnything\Setup\install.log`。

## B. 录音、波形和应用内试听

1. 进入“设置录音”，从页面内可见列表选择 Typeless 或微信语音输入法。
2. 按住遥控器麦克风键，并说“这是 RC003MS 真机录音测试”。
3. 持续按住时确认波形/电平和样本时长增长；说完后松开麦克风键。
4. 点击“试听录音”，不要打开资源管理器或外部播放器。
5. 亲自确认扬声器播放的是刚才说的话，再点击“听到自己的声音，继续”。

通过：按住时有实时波形和 Typeless 转写；应用内能听到清楚的人声；松开后录音和转写立即停止。

失败：按住时无波形/无转写、应用内听不到、听到静音/杂音，或松开后仍继续录音。保留
`%LOCALAPPDATA%\VoiceAnything\Recordings\remote-test-*.wav` 作为证据，不要用“样本数大于零”
代替人工听音结论。

## C. 输入法切换与记忆

1. 选择 Typeless，关闭并重开 VoiceAnything，确认列表仍选中 Typeless。
2. 将 Codex 或 Claude Code 输入框置于前台，按住麦克风键说话。
3. 确认 Typeless 开始转写；松开麦克风键后转写结束。
4. 改选微信语音输入法后重复。

通过：两种输入法均按选择激活；重启后选择不丢失；没有 PowerShell/CLI 窗口。

## D. 按真实遥控器图配置

1. 在连接页点击“直接设置按键”。
2. 分别点击渲染图中的电源、麦克风、方向环、OK、返回、主页、菜单、音量和 TV 键。
3. 确认点击位置与实体键一致；麦克风键显示固定的点按录音说明。
4. 选择 Codex 预设，为方向下设置“目录下滑”，OK 设置“发送任务”。
5. 为一个键分别设置单击、双击和长按；关闭并重开应用确认三项仍存在。
6. 切换 Claude Code 预设，确认 Skill 前缀为 `/`；切回 Codex 时为 `$`。

通过：所有热区可点击、所选动作立即显示并在重启后保持。

## E. Codex / Claude Code 前台动作

1. 完成一次语音和三个不同实体键的真机验证，点击“完成配置”。
2. 将 Codex 置于前台并打开一个有多项任务的界面。
3. 单击方向上/下，确认目录滚动；在输入框写一条无副作用测试消息，按 OK 发送。
4. 测试查找、回滚和 Skill 前缀；再在 Claude Code 终端重复。
5. 将记事本置于前台后按同一按键。

通过：受支持前台执行一次且仅一次；记事本等非允许进程不接收动作；双击不会额外触发单击。

## 自动化、Agent 与用户边界

- 自动化负责：协议解析、WAV 结构、应用内本地 URI、输入法枚举、设置持久化、手势去重、
  精确驱动/Gadget 指纹、服务固定端点、三项目构建和安装包发布。
- Agent 可负责：读取服务/安装日志、核对进程路径、截图检查布局、验证文件哈希和服务状态。
- 用户必须负责：批准唯一一次安装 UAC、按实体 RC003MS、亲耳确认录音、观察真实输入法切换，
  并确认 Codex/Claude Code 中动作语义符合预期。

回归命令：`Windows\scripts\test-baseline.ps1`。

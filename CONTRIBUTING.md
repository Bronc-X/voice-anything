# 贡献一个硬件型号

先确认硬件使用哪个传输适配器，再提交型号包。外观相似、蓝牙名称相同或发现 ATVV 服务，都不能单独证明兼容。

## 已有适配器

`xiaomi-atvv-v1` 目前接收 ATVV v0/v1 的 16 kHz ADPCM 音频与 HID 按键。Windows 按键桥还要求匹配现有 VID / PID / Revision、Windows 驱动指纹及唯一 WUDFHost。macOS 的当前发现链路仍受 RC001 / RC003 型号准入限制。新型号如果不符合这些条件，需要同时贡献适配器代码；不能只添加 JSON 并标为已支持。

型号包放在 `devices/<id>/`，包含 `profile.json` 及可选的本地 PNG/JPEG。参考 [RC003](devices/xiaomi-rc003/profile.json)。

```json
{
  "schemaVersion": 1,
  "id": "example-controller",
  "name": "Example controller",
  "adapter": "xiaomi-atvv-v1",
  "modelNumbers": ["EXAMPLE-1"],
  "artwork": null,
  "aspectRatio": 0.5,
  "capabilities": {"voice": false, "holdToTalk": false, "toggleVoice": false, "battery": false, "touch": false},
  "controls": [{"id": "CaptureNote", "label": "记笔记", "usage": 291, "x": 0.1, "y": 0.1, "width": 0.2, "height": 0.1, "gestures": ["single", "long"]}],
  "validation": {"windows": "research", "macos": "research"}
}
```

这是配置格式示例，不对应已支持的真实设备。`modelNumbers` 使用设备信息服务实际报告的完整型号，不做子串匹配。按钮坐标相对于外形图，范围为 0–1；ID 和 HID usage 必须唯一。语音键保留 ID `Microphone`，手势为 `voice`。其他 ID 可自行命名，未配置的动作默认关闭。

每个能力必须有实现与证据。当前型号协议不接受尚未实现的触摸、电量或切换收音能力。新的协议应另建适配器，把设备身份、按键边沿、音频格式、断开与重连转换为应用事件。不得通过移除现有驱动校验来扩大支持范围。

## 验证与提交

1. 运行 Windows 基线；macOS 运行 `swift test`。两端使用同一份型号文件。
2. 在应用“设备与型号”中导入包；核对型号、图片和每个热点。
3. 逐项测试按下/释放、重复报文、单击/双击/长按、松手停止语音、断开与重连。
4. 测试两只同型号设备，确认没有串键、串音；测试不支持的能力和错误型号明确报错。
5. 在 PR 中写明系统、硬件型号、固件、适配器和实际测试结果。不要提交蓝牙地址、个人转写、令牌、录音或系统日志。

`research` 表示待研究，`candidate` 表示已有实现待真机确认，`verified` 只用于有验收记录的平台。不要把导入成功、编译成功或模拟测试等同于真机通过。

代码、文档和型号配置按项目 GPL-3.0-only 贡献。图片须有可用于公开分发的授权，并说明来源。

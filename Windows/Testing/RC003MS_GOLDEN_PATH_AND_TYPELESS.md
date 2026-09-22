# Windows 音频与按键链路

这份说明保留此前 Windows 原型的工程约束；旧版的硬件验证不能代替新版验收。当前验收记录见 [docs/ACCEPTANCE.md](../../docs/ACCEPTANCE.md)。

## 三条独立证据

- 蓝牙：所选设备已配对，实际 DIS 型号和 ATVV 能力符合型号配置。
- 按键：HID 桥只处理所选设备，系统驱动与 Gadget 指纹均匹配。使用真实 press/release 报告确认动作。
- 音频：16 kHz、16-bit、mono PCM，经 AudioGraph 写入 VB-CABLE。在应用的录音测试页试听，不能仅凭样本数判断有效语音。

## 必须保留的行为

1. 先临时路由到 CABLE Output，再发目标听写快捷键；结束或失败后恢复原默认麦克风。
2. 实体键按下和松开决定听写生命周期。ATVV 在长按期间可能分段停止和重启，分段不能提前松开目标快捷键。
3. Codex 使用 Ctrl+Shift+D 按住语义；Typeless 使用可配置输入方案中的 LeftAlt 切换语义。用户软件的快捷键必须与所选方案一致。
4. 松开后迟到的语音流应关闭；退出、断连和换设备不能遗留按住状态。
5. PCM 线程入队，界面定时批量绘制声谱；正常听写不写 WAV，只有录音测试页写入测试文件。
6. 不支持的系统驱动必须明确拒绝桥接，不得取消校验。

## 可重跑检查

```powershell
./Windows/scripts/test-baseline.ps1
./Windows/scripts/publish-installer.ps1
```

自动检查验证协议、状态机、存储、MCP 和构建。真实音频、目标输入法、切换设备及安装卸载按验收文档逐项执行。

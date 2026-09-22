# 构建与运行

## Windows

要求 Windows 11 x64、.NET 10 SDK。普通开发构建不需要管理员权限：

```powershell
./Windows/scripts/test-baseline.ps1
dotnet run --project Windows/src/SayAll.Windows
```

SDK 没有加入 PATH 时，可给基线脚本设置 `SAYALL_DOTNET`，值为你自己的 `dotnet.exe` 路径。仓库不依赖开发者本机目录。

完整运行还需要自行安装 [VB-CABLE](https://vb-audio.com/Cable/)，在 Windows 设置中配对遥控器，再在应用“设备与型号”中选择、验证。声音通过 CABLE Input / CABLE Output 送入所选输入法；结束后恢复原默认麦克风。按键桥只接受已收录的系统驱动指纹，Windows 更新后若驱动不匹配会报错，不能关闭校验强行运行。

构建安装目录：

```powershell
./Windows/scripts/publish-installer.ps1 -GadgetPath '<解压后的官方 Frida Gadget DLL>'
```

脚本只接受官方 Frida Gadget 17.15.3 Windows x86_64，校验指纹后打包；`-OutputDirectory` 必须在仓库 `artifacts` 的子目录中。可从 [Frida 对应发布页](https://github.com/frida/frida/releases/tag/17.15.3) 获取文件。已有本项目旧版固定运行库时可省略参数。输出包含主程序、MCP Helper、后台服务、安装器与许可文件。

运行安装器需要管理员权限。它把二进制放入 Program Files，仅给型号选择目录授权普通用户修改；同时停用旧 VibeControl/SayAll 按键服务，避免争用同一设备。升级前退出旧主程序。安装行为与真实设备测试不包含在无硬件的自动检查中。

卸载前从托盘退出 Voice Anything，并断开 Agent 的 MCP 连接，再从 Windows“已安装的应用”卸载。卸载程序只清理本产品的服务、安装目录和开始菜单入口，保留本地回眸、统计、授权和设置，以及外部安装的音频驱动和共享 HID 运行库。卸载路径必须与安装位置一致，包含重解析点的目录会被拒绝。

## macOS

要求 macOS 14+、Xcode、Swift 6.2+ 和 .NET 10 SDK。默认构建只使用公开源代码；不需要上游的私有可选包。

```sh
cd macOS
swift test
bash scripts/build-app.sh
```

脚本在 `artifacts` 中生成独立的 `.app`，包含共享型号包、许可文件和对应架构的自包含 MCP Helper。GitHub 的 Apple Silicon macOS runner 已完成 Swift 测试、完整打包、签名结构校验及 Helper 运行检查；Intel 分支尚未单独验收。生成物使用临时签名，尚未进行 Developer ID 签名、公证和真机验收，是开发包。

自行安装兼容的虚拟音频设备，例如 [BlackHole 2ch](https://github.com/ExistentialAudio/BlackHole)，并授予应用蓝牙、输入监控和辅助功能权限。macOS 设备身份使用系统蓝牙 UUID；Windows 使用选中的蓝牙地址。不同系统的身份表示不会互相冒用。

## MCP 与共享契约检查

```sh
dotnet build Windows/src/VoiceAnything.Mcp
python scripts/check-mcp.py --dotnet dotnet --helper Windows/src/VoiceAnything.Mcp/bin/Debug/net10.0/VoiceAnything.Mcp.dll
```

这会启动真实 Helper 进程，检查握手、检索、撤销与损坏请求。测试数据在临时目录，运行后清除。Swift 和 C# 的共享存储样例放在 `docs/fixtures`。

## 原生界面截图

```powershell
dotnet build Windows/src/SayAll.Windows
& ./Windows/src/SayAll.Windows/bin/Debug/net10.0-windows10.0.26100.0/win-x64/VoiceAnything.exe --capture-previews ./docs/images
```

macOS 完整打包后，在仓库根目录执行：

```sh
app="$(cat artifacts/macos-app-path.txt)"
VOICE_ANYTHING_PREVIEW_DIR="$PWD/artifacts/macos-previews" "$app/Contents/MacOS/RemoteMic"
```

这两种模式分别实例化真实 WPF 和 SwiftUI/AppKit 界面，使用独立临时数据和可见的预览标记，不启动硬件链路、不会更改个人输入法或快捷键配置。它们可以验收界面渲染，不能代替实机连接、语音转写或设备切换验收。

README 同时保留原生截图和基于原图制作的展示封面，详见[截图说明](SCREENSHOTS.md)。

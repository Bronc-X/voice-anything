# 截图来源

`images/windows-*.png` 来自本地编译的 WPF 应用；`images/macos-*.png` 来自 GitHub macOS runner 上打包后的 SwiftUI/AppKit 应用。使用程序自带的预览入口和临时演示数据，不连接硬件，不读取个人历史。复现命令见 [BUILD.md](BUILD.md)。

截图只证明界面已运行和渲染，不能证明真实设备连接成功。记录与状态页保留演示标记。

`images/readme-hero.png` 使用内置 imagegen 工具，将 Windows 原生截图排版为 README 展示封面。原图另外保留，验收以原图和实际程序为准。工具没有提供可选择或可核验的模型标识，因此不将该图片标为 GPT Image 2.5。

生成提示词：

> Use case: compositing. Asset type: polished GitHub README hero for Voice Anything. Input image 1 is a REAL native Windows application screenshot; use it as the exact source panel, never invent a UI or alter any UI text, control, number, hardware shape, or feature. Create a beautifully restrained premium software presentation in a wide 16:10 landscape composition, high resolution. Matte ice-blue to white backdrop, subtle soft studio shadow under the screenshot panel, crisp front-facing screenshot with no perspective distortion, ample quiet margins. Screenshot should occupy most of the canvas, remain fully visible, legible, and keep its original aspect ratio. Above it set only the exact title 'Voice Anything' in strong clean dark ink typography and the small exact subline 'Windows + macOS'. Avoid extra objects, new icons, logos, devices, fake feature labels, badges, watermarks and decorative clutter. Preserve the screenshot's visible preview disclaimer and all its contents. This is a screenshot-based presentation, not evidence of connected hardware.

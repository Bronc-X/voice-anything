using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SayAll.Core.History;

namespace SayAll.Windows;

/// <summary>Renders the actual WPF windows using isolated, explicitly labelled demonstration data.</summary>
internal static class NativePreviews
{
    public static async Task CaptureAsync(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var temporary = Path.Combine(Path.GetTempPath(), "VoiceAnything.Previews", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var store = new JournalStore(temporary);
            store.SetPrivacy(true, false);
            var today = DateTimeOffset.Now;
            for (var day = 0; day < 7; day++)
            {
                var at = today.AddDays(-day).AddHours(-1);
                for (var count = 0; count < 24 + day * 8; count++) store.RecordButton(at, TimeZoneInfo.Local);
                store.RecordVoice(at, at.AddMinutes(3 + day), 180 + day * 60, TimeZoneInfo.Local);
            }
            var examples = new[]
            {
                ("Codex", "把今天讨论的设备接入方案整理成文档，标明型号、协议和仍需验证的能力。"),
                ("Notepad", "下一个版本先把连接、按键映射和语音输入跑通，再逐项验收。"),
                ("Codex", "为新的遥控器添加型号配置，让界面显示它自己的外形和可用按键。"),
                ("Notepad", "灵感来自一次简单的按下：让手里的硬件，接入自己习惯的工作方式。"),
            };
            for (var i = 0; i < examples.Length; i++)
                store.Append(new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), today.AddMinutes(-i * 17 - 1),
                    today.AddMinutes(-i * 17), examples[i].Item1, "xiaomi-rc003", examples[i].Item2, "manual"));

            var main = new MainWindow(preview: true);
            main.Show();
            await ReadyAsync(main);
            var television = Descendants<Button>(main).First(button => Equals(button.Tag, "Tv"));
            television.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ReadyAsync(main);
            Save(main, Path.Combine(outputDirectory, "windows-device-mapping.png"));
            main.Close();

            var insights = new InsightsWindow(store, preview: true);
            insights.Show();
            await ReadyAsync(insights);
            var tabs = Descendants<TabControl>(insights).Single();
            var week = Descendants<Button>(insights).First(button => Equals(button.Tag, "week"));
            week.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ReadyAsync(insights);
            Save(insights, Path.Combine(outputDirectory, "windows-statistics.png"));
            tabs.SelectedIndex = 1;
            await ReadyAsync(insights);
            Save(insights, Path.Combine(outputDirectory, "windows-reflections.png"));
            tabs.SelectedIndex = 2;
            await ReadyAsync(insights);
            Save(insights, Path.Combine(outputDirectory, "windows-agent.png"));
            insights.Close();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
    private static async Task ReadyAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
        window.UpdateLayout();
    }
    private static void Save(Window window, string path)
    {
        const double scale = 2;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        bitmap.Render(background);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}

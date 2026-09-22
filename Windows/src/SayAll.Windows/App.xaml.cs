using System.ComponentModel;
using System.Drawing;
using System.Windows;

namespace SayAll.Windows;

public partial class App : Application
{
    private VoiceAnythingSingleInstance? singleInstance;
    private readonly TrayClosePolicy trayClosePolicy = new();
    private MainWindow? mainWindow;
    private System.Windows.Forms.NotifyIcon? trayIcon;
    private System.Windows.Forms.ContextMenuStrip? trayMenu;
    private Icon? trayDrawingIcon;
    private bool closeHintShown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--capture-previews", var outputDirectory])
        {
            base.OnStartup(e);
            try { await NativePreviews.CaptureAsync(outputDirectory); Shutdown(0); }
            catch (Exception error)
            {
                System.IO.Directory.CreateDirectory(outputDirectory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(outputDirectory, "capture-error.txt"), error.ToString());
                Shutdown(1);
            }
            return;
        }
        singleInstance = VoiceAnythingSingleInstance.TryAcquire();
        if (singleInstance is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        InitializeTrayIcon();

        mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Closing += MainWindow_Closing;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        trayMenu?.Dispose();
        trayMenu = null;
        trayDrawingIcon?.Dispose();
        trayDrawingIcon = null;
        singleInstance?.Dispose();
        singleInstance = null;
        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
        trayDrawingIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ??
            (Icon)SystemIcons.Application.Clone();
        trayMenu = new System.Windows.Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
        };

        var openItem = new System.Windows.Forms.ToolStripMenuItem("打开 VoiceAnything")
        {
            Font = new Font(
                System.Windows.Forms.Control.DefaultFont,
                System.Drawing.FontStyle.Bold),
        };
        openItem.Click += (_, _) => Dispatcher.InvokeAsync(ShowMainWindow);
        trayMenu.Items.Add(openItem);
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("完全退出");
        exitItem.Click += (_, _) => Dispatcher.InvokeAsync(ExitFromTray);
        trayMenu.Items.Add(exitItem);

        trayIcon = new System.Windows.Forms.NotifyIcon
        {
            ContextMenuStrip = trayMenu,
            Icon = trayDrawingIcon,
            Text = "VoiceAnything · RC003MS 后台运行中",
            Visible = true,
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.InvokeAsync(ShowMainWindow);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!trayClosePolicy.ShouldCancelClose || mainWindow is null)
        {
            return;
        }

        e.Cancel = true;
        mainWindow.Hide();
        if (closeHintShown || trayIcon is null)
        {
            return;
        }

        closeHintShown = true;
        trayIcon.ShowBalloonTip(
            2500,
            "VoiceAnything 仍在运行",
            "遥控器功能已转入后台。可从系统托盘打开或完全退出。",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void ShowMainWindow()
    {
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        _ = mainWindow.Activate();
    }

    private void ExitFromTray()
    {
        trayClosePolicy.RequestExit();
        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
        }

        if (mainWindow is null)
        {
            Shutdown();
            return;
        }

        mainWindow.Close();
    }
}

public sealed class TrayClosePolicy
{
    private bool exitRequested;

    public bool ShouldCancelClose => !exitRequested;

    public void RequestExit()
    {
        exitRequested = true;
    }
}

public sealed class VoiceAnythingSingleInstance : IDisposable
{
    private const string DefaultName = @"Local\VoiceAnything.Main";
    private Mutex? mutex;

    private VoiceAnythingSingleInstance(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public static VoiceAnythingSingleInstance? TryAcquire(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var candidate = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (!createdNew)
        {
            candidate.Dispose();
            return null;
        }

        return new VoiceAnythingSingleInstance(candidate);
    }

    public void Dispose()
    {
        var current = Interlocked.Exchange(ref mutex, null);
        if (current is null)
        {
            return;
        }

        try
        {
            current.ReleaseMutex();
        }
        finally
        {
            current.Dispose();
        }
    }
}

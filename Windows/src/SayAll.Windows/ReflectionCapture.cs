using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using SayAll.Core.History;

namespace SayAll.Windows;

/// <summary>Captures one explicitly enabled voice insertion via the public accessibility API.</summary>
internal sealed class ReflectionCapture : IDisposable
{
    private readonly JournalStore store;
    private readonly Action<string> status;
    private readonly KeyboardObserver keyboard = new();
    private CancellationTokenSource? lifetime;
    private Task<CaptureTarget?>? targetTask;
    private string? sessionId;
    private string model = "";
    private DateTimeOffset startedAt;
    private long keyboardRevision;
    private static int accessibilityBusy;

    public ReflectionCapture(JournalStore store, Action<string> status)
    { this.store = store; this.status = status; }

    public void Start(string deviceModel)
    {
        Cancel();
        if (!store.Read().RecordReflections) return;
        if (!keyboard.Available) { status("回眸：无法识别手动键入，本次未启用记录"); return; }
        lifetime = new CancellationTokenSource();
        sessionId = Guid.NewGuid().ToString();
        model = deviceModel;
        startedAt = DateTimeOffset.Now;
        keyboardRevision = keyboard.Revision;
        // UI Automation must run on an MTA worker; a hung provider must not block dictation.
        var snapshot = ReadAccessibility(CaptureTarget.TryCreate);
        targetTask = snapshot.Wait(250) ? snapshot : Task.FromResult<CaptureTarget?>(null);
        status("回眸：正在等待本次语音输入结果");
    }

    public async Task FinishAsync()
    {
        if (lifetime is null || targetTask is null || sessionId is null) return;
        var cancellation = lifetime.Token;
        var captureTask = targetTask;
        var capturedSession = sessionId;
        var capturedStart = startedAt;
        var capturedModel = model;
        var revision = keyboardRevision;
        targetTask = null;
        try
        {
            var target = await captureTask;
            if (target is null) { status("回眸：此输入框不支持安全提取，本次未记录"); return; }
            string? previous = null;
            var stable = 0;
            for (var attempt = 0; attempt < 34; attempt++)
            {
                await Task.Delay(300, cancellation);
                if (keyboard.Revision != revision) { status("回眸：检测到手动键入，本次未记录"); return; }
                var inserted = await ReadAccessibility(target.ReadInsertion).WaitAsync(TimeSpan.FromMilliseconds(500), cancellation);
                if (string.IsNullOrWhiteSpace(inserted)) { previous = null; stable = 0; continue; }
                stable = inserted == previous ? stable + 1 : 0;
                previous = inserted;
                if (stable < 3) continue;
                cancellation.ThrowIfCancellationRequested();
                var record = new Reflection(Guid.NewGuid().ToString(), capturedSession, capturedStart,
                    DateTimeOffset.Now, target.Application, capturedModel, inserted, "accessibility-delta-v1");
                var saved = await Task.Run(() =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    return store.Append(record);
                }, cancellation);
                status(saved ? "回眸：本次输入已保存在本机" : "回眸：记录已关闭或本次会话已保存");
                return;
            }
            status("回眸：未获得稳定的新文字，本次未记录");
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { status("回眸：输入框响应超时，本次未记录"); }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidOperationException or ElementNotAvailableException or COMException)
        { status("回眸：本次未保存，请检查输入框支持情况或本地存储。"); }
    }

    public void Cancel()
    { lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; targetTask = null; sessionId = null; }
    public void Dispose() { Cancel(); keyboard.Dispose(); }

    private static Task<T?> ReadAccessibility<T>(Func<T?> read) where T : class
    {
        if (Interlocked.CompareExchange(ref accessibilityBusy, 1, 0) != 0) return Task.FromResult<T?>(null);
        return Task.Run(() => { try { return read(); } finally { Volatile.Write(ref accessibilityBusy, 0); } });
    }

    private sealed class CaptureTarget(AutomationElement element, string before, int selectionStart, int selectionLength, string application)
    {
        public string Application { get; } = application;
        public static CaptureTarget? TryCreate()
        {
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element is null || element.Current.IsPassword || element.Current.ControlType != ControlType.Edit ||
                    element.Current.ProcessId == Environment.ProcessId) return null;
                using var process = Process.GetProcessById(element.Current.ProcessId);
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                {
                    var text = (TextPattern)pattern;
                    var before = text.DocumentRange.GetText(JournalStore.MaximumTextLength + 1);
                    if (before.Length > JournalStore.MaximumTextLength) return null;
                    var selection = text.GetSelection();
                    if (selection.Length != 1) return null;
                    var prefix = text.DocumentRange.Clone();
                    prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.Start);
                    var start = prefix.GetText(JournalStore.MaximumTextLength + 1).Length;
                    var length = selection[0].GetText(JournalStore.MaximumTextLength + 1).Length;
                    if (start + length > before.Length) return null;
                    return new(element, before, start, length, process.ProcessName);
                }
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && ((ValuePattern)value).Current.Value.Length == 0)
                    return new(element, "", 0, 0, process.ProcessName);
                return null;
            }
            catch (Exception exception) when (exception is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
            { return null; }
        }

        public string? ReadInsertion()
        {
            try
            {
                if (!Automation.Compare(element, AutomationElement.FocusedElement) || element.Current.IsPassword) return null;
                string after;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
                    after = ((TextPattern)text).DocumentRange.GetText(JournalStore.MaximumTextLength + 1);
                else if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
                    after = ((ValuePattern)value).Current.Value;
                else return null;
                return TranscriptDelta.Extract(before, after, selectionStart, selectionLength);
            }
            catch (Exception exception) when (exception is ElementNotAvailableException or COMException or InvalidOperationException)
            { return null; }
        }
    }

    private sealed class KeyboardObserver : IDisposable
    {
        private readonly HookProc callback;
        private readonly IntPtr hook;
        private long revision;
        public long Revision => Interlocked.Read(ref revision);
        public KeyboardObserver()
        {
            callback = Observe;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            // If observation is unavailable, invalidate every attempt rather than capture ambiguous text.
        }
        private IntPtr Observe(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && (message == (IntPtr)0x0100 || message == (IntPtr)0x0104))
            {
                var input = Marshal.PtrToStructure<KeyboardData>(data);
                if ((input.Flags & 0x10) == 0 && (input.Key is >= 0x30 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xE2 or 0x08 or 0x09 or 0x0D or 0x20 or 0x2E))
                    Interlocked.Increment(ref revision);
            }
            return CallNextHookEx(hook, code, message, data);
        }
        public bool Available => hook != IntPtr.Zero;
        public void Dispose() { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardData
        { public uint Key; public uint Scan; public uint Flags; public uint Time; public UIntPtr Extra; }
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    }
}

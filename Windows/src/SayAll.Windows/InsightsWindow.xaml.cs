using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SayAll.Core.History;

namespace SayAll.Windows;

public partial class InsightsWindow : Window
{
    private readonly JournalStore store;
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string range = "day";
    private int resultOffset;
    private int generation;
    private readonly bool preview;
    public event Action? PrivacyChanged;

    public InsightsWindow(JournalStore store, bool preview = false)
    {
        this.preview = preview;
        this.store = store;
        InitializeComponent();
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await ReloadAsync(); };
        Loaded += async (_, _) => await ReloadAsync();
        Closed += (_, _) => { searchTimer.Stop(); generation++; McpConfigBox.Clear(); };
    }

    private async Task ReloadAsync()
    {
        var request = ++generation;
        var query = SearchBox.Text;
        var application = ApplicationBox.Text.Trim();
        var from = FromDate.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var through = ThroughDate.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var today = DateTime.Today;
        var start = range switch { "day" => today, "week" => today.AddDays(-((int)today.DayOfWeek + 6) % 7), _ => (DateTime?)null };
        StatusText.Text = "正在读取本地记录…";
        try
        {
            var result = await Task.Run(() => (Document: store.Read(),
                Usage: store.Summarize(start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Records: store.Search(query, application, from, through, 50, resultOffset)));
            if (request != generation) return;
            RecordCheck.IsChecked = result.Document.RecordReflections;
            AgentCheck.IsChecked = result.Document.AgentAccessEnabled;
            GrantList.ItemsSource = result.Document.Grants;
            ButtonCountText.Text = result.Usage.ButtonPresses.ToString("N0");
            VoiceTimeText.Text = result.Usage.VoiceSeconds >= 60
                ? $"{result.Usage.VoiceSeconds / 60:N1} 分" : $"{result.Usage.VoiceSeconds:N0} 秒";
            SessionCountText.Text = result.Usage.VoiceSessions.ToString("N0");
            UsageGrid.ItemsSource = result.Usage.Days.Reverse();
            UsageEmpty.Visibility = result.Usage.Days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ReflectionGrid.ItemsSource = result.Records;
            ReflectionsEmpty.Visibility = result.Records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ReflectionsEmpty.Text = query.Length > 0 || application.Length > 0 || from is not null || through is not null
                ? "没有找到匹配的回眸。试试其他关键词、应用名称或日期。"
                : "尚无回眸记录。开启记录后，在支持的输入框中完成一次语音输入。";
            MoreButton.IsEnabled = result.Records.Count == 50 && resultOffset + 50 < JournalStore.MaximumReflections;
            PreviousButton.IsEnabled = resultOffset > 0;
            StatusText.Text = (preview ? "演示数据 · " : "") + $"第 {resultOffset / 50 + 1} 页 · {result.Records.Count} 条回眸 · 所有数据保存在本机";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { if (request == generation) StatusText.Text = "读取失败：" + exception.Message; }
    }

    private async void Range_Click(object sender, RoutedEventArgs e)
    {
        range = (string)((Button)sender).Tag;
        RangeLabel.Text = range switch { "week" => "本周 · 周一至今", "all" => "全部记录", _ => "今天" };
        await ReloadAsync();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void Search_Changed(object sender, TextChangedEventArgs e)
    { if (!IsLoaded) return; resultOffset = 0; searchTimer.Stop(); searchTimer.Start(); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e)
    { if (!IsLoaded) return; resultOffset = 0; searchTimer.Stop(); searchTimer.Start(); }
    private async void More_Click(object sender, RoutedEventArgs e) { resultOffset += 50; await ReloadAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { resultOffset = Math.Max(0, resultOffset - 50); await ReloadAsync(); }
    private async void Privacy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            store.SetPrivacy(RecordCheck.IsChecked == true, AgentCheck.IsChecked == true);
            PrivacyChanged?.Invoke();
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { StatusText.Text = "设置未保存：" + exception.Message; }
    }
    private void CopyReflection_Click(object sender, RoutedEventArgs e)
    {
        if (ReflectionGrid.SelectedItem is not Reflection record) { StatusText.Text = "请先选择一条回眸。"; return; }
        CopyText(record.Text);
    }
    private async void ExportReflection_Click(object sender, RoutedEventArgs e)
    {
        if (ReflectionGrid.SelectedItem is not Reflection record) { StatusText.Text = "请先选择一条回眸。"; return; }
        var dialog = new SaveFileDialog { FileName = $"reflection-{record.EndedAt:yyyyMMdd-HHmmss}.json", Filter = "JSON 文件|*.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(record, JournalStore.JsonOptions)); StatusText.Text = "回眸已导出。"; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { StatusText.Text = "导出失败：" + exception.Message; }
    }
    private async void DeleteReflection_Click(object sender, RoutedEventArgs e)
    {
        if (ReflectionGrid.SelectedItem is not Reflection record) { StatusText.Text = "请先选择一条回眸。"; return; }
        if (MessageBox.Show(this, "删除这条回眸后无法恢复。继续删除？", "删除回眸", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { store.DeleteReflection(record.Id); await ReloadAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { StatusText.Text = "删除失败：" + exception.Message; }
    }
    private async void Grant_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "VoiceAnything.Mcp.exe");
            if (!File.Exists(executable)) throw new InvalidOperationException("当前开发目录没有 MCP Helper，请先构建完整发布包。");
            var grant = store.GrantAgent(ClientNameBox.Text);
            McpConfigBox.Text = JsonSerializer.Serialize(new
            {
                mcpServers = new Dictionary<string, object> { ["voice-anything"] = new
                { command = executable, env = new Dictionary<string, string> { ["VOICE_ANYTHING_MCP_TOKEN"] = grant.Token } } },
            }, new JsonSerializerOptions { WriteIndented = true });
            await ReloadAsync();
            StatusText.Text = "授权已创建。复制连接配置到客户端；关闭此窗口后不会再显示授权码。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { StatusText.Text = "创建失败：" + exception.Message; }
    }
    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (GrantList.SelectedItem is not AgentGrant grant) { StatusText.Text = "请先选择一个客户端。"; return; }
        try { store.RevokeAgent(grant.Id); McpConfigBox.Clear(); await ReloadAsync(); StatusText.Text = "授权已撤销，已有连接也不能继续读取。"; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { StatusText.Text = "撤销失败：" + exception.Message; }
    }
    private void CopyConfig_Click(object sender, RoutedEventArgs e)
    { if (McpConfigBox.Text.Length > 0) CopyText(McpConfigBox.Text); else StatusText.Text = "请先生成连接配置。"; }
    private void CopyText(string text)
    {
        try { Clipboard.SetText(text); StatusText.Text = "已复制。"; }
        catch (System.Runtime.InteropServices.COMException) { StatusText.Text = "剪贴板正忙，请重试。"; }
    }
}

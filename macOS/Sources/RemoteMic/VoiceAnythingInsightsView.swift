import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct VoiceAnythingInsightsView: View {
    @ObservedObject var journal: VoiceAnythingJournal
    @ObservedObject var settings: AppSettings
    var initialTab = 0
    @State private var tab = 0
    @State private var period = "day"
    @State private var query = ""
    @State private var application = ""
    @State private var from = ""
    @State private var through = ""
    @State private var page = 0
    @State private var selected: String?
    @State private var deleting: VAReflection?
    @State private var clientName = "我的 Agent"
    @State private var config = ""
    @State private var message = ""

    private var days: [VADay] {
        let today = VoiceAnythingJournal.dateKey(Date())
        let weekday = Calendar.current.component(.weekday, from: Date())
        let monday = Calendar.current.date(byAdding: .day, value: -((weekday + 5) % 7), to: Date())!
        let start = period == "day" ? today : period == "week" ? VoiceAnythingJournal.dateKey(monday) : ""
        return journal.document.days.filter { $0.date >= start && $0.date <= today }.sorted { $0.date > $1.date }
    }
    private var validDates: Bool {
        [from, through].allSatisfy { $0.isEmpty || VoiceAnythingJournal.validDate($0) } &&
            (from.isEmpty || through.isEmpty || from <= through)
    }
    private var records: [VAReflection] {
        guard validDates else { return [] }
        return journal.newestReflections.filter { record in
            (query.isEmpty || record.text.localizedCaseInsensitiveContains(query)) &&
            (application.isEmpty || record.application.caseInsensitiveCompare(application) == .orderedSame) &&
            (from.isEmpty || String(record.endedAt.prefix(10)) >= from) &&
            (through.isEmpty || String(record.endedAt.prefix(10)) <= through)
        }
    }
    private var selection: VAReflection? { records.first { $0.id == selected } }
    private var voiceDurationText: String {
        let seconds = days.reduce(0) { $0 + $1.voiceSeconds }
        return seconds >= 60 ? String(format: "%.1f 分", seconds / 60) : String(format: "%.0f 秒", seconds)
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            HStack {
                VStack(alignment: .leading, spacing: 5) {
                    Text("统计 · 回眸 · Agent").font(.system(size: 28, weight: .semibold))
                    Text("统计和回眸保存在这台电脑。是否记录、授权哪些 Agent，由你决定。").foregroundStyle(.secondary)
                }
                Spacer()
                Button("刷新") { journal.reload() }
            }
            Picker("页面", selection: $tab) {
                Text("使用统计").tag(0)
                Text("回眸").tag(1)
                Text("Agent 访问").tag(2)
            }.pickerStyle(.segmented)
            Group {
                if tab == 0 { usagePage }
                else if tab == 1 { reflectionsPage }
                else { agentPage }
            }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            Text(message.isEmpty ? journal.status : message).font(.caption).foregroundStyle(.secondary)
        }
        .padding(28)
        .onAppear { tab = initialTab; journal.reload() }
        .onDisappear { config = "" }
        .onChange(of: query) { _, _ in page = 0; selected = nil }
        .onChange(of: application) { _, _ in page = 0; selected = nil }
        .onChange(of: from) { _, _ in page = 0; selected = nil }
        .onChange(of: through) { _, _ in page = 0; selected = nil }
        .onChange(of: journal.document.reflections.count) { _, _ in page = min(page, max(0, (records.count - 1) / 50)) }
        .alert("删除这条回眸？", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } })) {
            Button("取消", role: .cancel) { deleting = nil }
            Button("删除", role: .destructive) { if let deleting { journal.delete(id: deleting.id) }; deleting = nil; selected = nil }
        } message: { Text("删除后无法恢复。") }
    }
    private var usagePage: some View {
        VStack(alignment: .leading, spacing: 20) {
            Picker("统计范围", selection: $period) {
                Text("今天").tag("day"); Text("本周 · 周一至今").tag("week"); Text("全部").tag("all")
            }.pickerStyle(.segmented).frame(maxWidth: 420)
            HStack(spacing: 16) {
                metric("按键次数", "\(days.reduce(Int64(0)) { $0 + $1.buttonPresses })", "每次实际按下计算一次")
                metric("语音时长", voiceDurationText, "按实际收到的音频计算")
                metric("语音会话", "\(days.reduce(Int64(0)) { $0 + $1.voiceSessions })", "连续音频分段归为一次")
            }
            Text("每日记录").font(.headline)
            if days.isEmpty { empty("还没有使用记录。连接遥控器后，按键和语音会自动记在这里。") }
            else {
                Table(days) {
                    TableColumn("日期", value: \.date)
                    TableColumn("按键") { Text("\($0.buttonPresses)") }
                    TableColumn("语音秒数") { Text(String(format: "%.1f", $0.voiceSeconds)) }
                    TableColumn("会话") { Text("\($0.voiceSessions)") }
                }
            }
        }
    }
    private func metric(_ title: String, _ value: String, _ subtitle: String) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).foregroundStyle(.secondary)
            Text(value).font(.system(size: 34, weight: .semibold, design: .rounded))
            Text(subtitle).font(.caption).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity, alignment: .leading).padding(22)
            .background(.background, in: RoundedRectangle(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.primary.opacity(0.08)))
    }
    private var reflectionsPage: some View {
        VStack(alignment: .leading, spacing: 16) {
            Toggle("保存 Voice Anything 触发的语音输入结果", isOn: Binding(
                get: { journal.document.recordReflections }, set: { enabled in
                    journal.setPrivacy(reflections: enabled, agents: journal.document.agentAccessEnabled) { error in
                        if error == nil { settings.localTranscriptHistoryEnabled = enabled }
                    }
                }))
            Text("默认关闭。只记录本次输入的新增文字；不支持的输入框不会记录。")
                .font(.caption).foregroundStyle(.secondary)
            HStack {
                TextField("搜索回眸文字", text: $query)
                TextField("应用名称（精确匹配）", text: $application).frame(width: 170)
            }
            HStack {
                TextField("开始日期 YYYY-MM-DD", text: $from)
                TextField("结束日期 YYYY-MM-DD", text: $through)
            }
            if !validDates { Text("日期范围无效，请使用 YYYY-MM-DD。").foregroundStyle(.red) }
            if records.isEmpty { empty("没有找到回眸记录。开启记录并完成一次语音输入，或调整搜索条件。") }
            else {
                Table(Array(records.dropFirst(page * 50).prefix(50)), selection: $selected) {
                    TableColumn("时间") { record in
                        Text(VoiceAnythingJournal.instant(record.endedAt)?.formatted(date: .numeric, time: .shortened) ?? record.endedAt)
                            .help(record.endedAt)
                    }.width(170)
                    TableColumn("应用", value: \.application).width(110)
                    TableColumn("输入的文字") { Text($0.text).lineLimit(4).padding(.vertical, 8) }
                }
            }
            HStack {
                Button("复制选中") { if let selection { copy(selection.text) } }.disabled(selection == nil)
                Button("导出选中") { exportSelection() }.disabled(selection == nil)
                Button("删除选中") { deleting = selection }.disabled(selection == nil)
                Spacer()
                Button("上一页") { page -= 1 }.disabled(page == 0)
                Text("第 \(page + 1) 页").foregroundStyle(.secondary)
                Button("下一页") { page += 1 }.disabled((page + 1) * 50 >= records.count)
            }
        }.textFieldStyle(.roundedBorder)
    }
    private var agentPage: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text("授权 Agent 读取回眸与统计").font(.title2.weight(.semibold))
                Text("本地只读 MCP：搜索回眸、读取单条记录、列出应用、查询统计。").foregroundStyle(.secondary)
                Toggle("允许已授权的本地 Agent 读取", isOn: Binding(
                    get: { journal.document.agentAccessEnabled }, set: { journal.setPrivacy(reflections: journal.document.recordReflections, agents: $0) }))
                Text("关闭或撤销授权后，已有连接也会停止读取。第三方 Agent 可能把内容发送到其云端模型。")
                    .font(.caption).foregroundStyle(.secondary)
                HStack { TextField("客户端名称", text: $clientName); Button("生成连接配置") { grant() } }
                ForEach(journal.document.grants) { grant in
                    HStack { Text(grant.name); Spacer(); Button("撤销") { journal.revoke(id: grant.id); config = "" } }
                }
                Divider()
                Text("连接配置").font(.headline)
                Text("授权码只在本次窗口中显示，请勿分享或提交到仓库。").font(.caption).foregroundStyle(.secondary)
                Text(config.isEmpty ? "生成授权后，在这里复制连接配置。" : config)
                    .font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                    .frame(maxWidth: .infinity, minHeight: 140, alignment: .topLeading).padding(16)
                    .background(.background, in: RoundedRectangle(cornerRadius: 12))
                Button("复制连接配置") { copy(config) }.disabled(config.isEmpty)
            }.textFieldStyle(.roundedBorder)
        }
    }
    private func empty(_ text: String) -> some View {
        Text(text).foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity).padding(28)
    }
    private func copy(_ text: String) {
        NSPasteboard.general.clearContents()
        message = NSPasteboard.general.setString(text, forType: .string) ? "已复制。" : "复制失败，请重试。"
    }
    private func exportSelection() {
        guard let selection else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = "reflection-\(selection.id).json"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try JSONEncoder().encode(selection).write(to: url, options: .atomic); message = "回眸已导出。" }
        catch { message = "导出失败：\(error.localizedDescription)" }
    }
    private func grant() {
        let helper = Bundle.main.bundleURL.appendingPathComponent("Contents/Helpers/VoiceAnything.Mcp")
        guard FileManager.default.isExecutableFile(atPath: helper.path) else { message = "当前应用没有打包 MCP Helper，请运行完整构建脚本。"; return }
        journal.grant(name: clientName) { result in
            switch result {
            case .failure(let error): message = error.localizedDescription
            case .success(let token):
                let value: [String: Any] = ["mcpServers": ["voice-anything": ["command": helper.path,
                    "env": ["VOICE_ANYTHING_MCP_TOKEN": token]]]]
                do {
                    config = String(decoding: try JSONSerialization.data(withJSONObject: value, options: [.prettyPrinted, .sortedKeys]), as: UTF8.self)
                    message = "授权已创建。复制连接配置到客户端。"
                } catch { message = "连接配置生成失败。请撤销本次授权后重试。" }
            }
        }
    }
}

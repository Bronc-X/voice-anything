import AppKit
import SwiftUI

@MainActor
enum VoiceAnythingNativePreviews {
    static func capture(to output: URL) throws {
        let temporary = FileManager.default.temporaryDirectory.appendingPathComponent("VoiceAnything.Preview-\(UUID().uuidString)")
        let suite = "VoiceAnything.Preview.\(UUID().uuidString)"
        guard let defaults = UserDefaults(suiteName: suite) else { throw VAStorageError.invalid }
        defer {
            defaults.removePersistentDomain(forName: suite)
            try? FileManager.default.removeItem(at: temporary)
        }
        let settings = AppSettings(defaults: defaults)
        settings.applicationLanguage = .simplifiedChinese
        let journal = VoiceAnythingJournal(directory: temporary)
        journal.setPrivacy(reflections: true, agents: false)
        let now = Date()
        for day in 0..<7 {
            let start = now.addingTimeInterval(Double(-day * 86_400 - 600))
            for _ in 0..<(24 + day * 8) { journal.recordButton(at: start) }
            journal.recordVoice(start: start, end: start.addingTimeInterval(180), audioSeconds: 180)
        }
        for (index, text) in [
            "把今天讨论的设备接入方案整理成文档，标明型号、协议和仍需验证的能力。",
            "下一个版本先把连接、按键映射和语音输入跑通，再逐项验收。",
            "为新的遥控器添加型号配置，让界面显示它自己的外形和可用按键。",
        ].enumerated() {
            let end = now.addingTimeInterval(Double(-index * 1020))
            journal.append(sessionID: UUID(), start: end.addingTimeInterval(-30), end: end,
                application: index == 1 ? "TextEdit" : "Codex", model: "xiaomi-rc003", text: text)
        }
        journal.flush()
        _ = NSApplication.shared
        NSApp.setActivationPolicy(.accessory)
        NSApp.appearance = NSAppearance(named: .aqua)
        RunLoop.current.run(until: Date().addingTimeInterval(0.2))
        let localization = LocalizationStore(settings: settings)
        let pages: [(String, AnyView)] = [
            ("macos-devices", AnyView(VoiceAnythingDevicesView(settings: settings))),
            ("macos-statistics", AnyView(VoiceAnythingInsightsView(journal: journal, settings: settings, initialTab: 0))),
            ("macos-reflections", AnyView(VoiceAnythingInsightsView(journal: journal, settings: settings, initialTab: 1))),
            ("macos-agent", AnyView(VoiceAnythingInsightsView(journal: journal, settings: settings, initialTab: 2))),
        ]
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        for (name, page) in pages {
            let size = NSSize(width: 1120, height: 800)
            let root = VStack(spacing: 0) {
                page
                Text("演示数据 · 原生界面预览 · 未连接硬件").font(.caption).foregroundStyle(.secondary).padding(14)
            }.environmentObject(localization).frame(width: size.width, height: size.height)
                .background(Color(nsColor: .windowBackgroundColor))
            let window = NSWindow(contentRect: NSRect(origin: .zero, size: size), styleMask: [.borderless], backing: .buffered, defer: false)
            window.contentViewController = NSHostingController(rootView: root)
            window.orderFront(nil)
            RunLoop.current.run(until: Date().addingTimeInterval(0.5))
            guard let view = window.contentView else { throw VAStorageError.invalid }
            view.layoutSubtreeIfNeeded()
            view.displayIfNeeded()
            guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw VAStorageError.invalid }
            view.cacheDisplay(in: view.bounds, to: bitmap)
            guard let png = bitmap.representation(using: .png, properties: [:]) else { throw VAStorageError.invalid }
            try png.write(to: output.appendingPathComponent(name + ".png"))
            window.orderOut(nil)
            window.contentViewController = nil
        }
    }
}

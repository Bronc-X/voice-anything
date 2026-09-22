import Foundation
import Testing
@testable import RemoteMic

struct VoiceAnythingContractTests {
    private var repository: URL {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
    }
    private func temporaryDirectory() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("VoiceAnything.Tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }
    private func read(_ directory: URL) throws -> VAJournalDocument {
        try JSONDecoder().decode(VAJournalDocument.self, from: Data(contentsOf: directory.appendingPathComponent("journal.json")))
    }

    @Test func sharedFixtureSurvivesNativeReadAndWrite() throws {
        let directory = try temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        try FileManager.default.copyItem(at: repository.appendingPathComponent("docs/fixtures/journal-v1.json"),
            to: directory.appendingPathComponent("journal.json"))
        let journal = VoiceAnythingJournal(directory: directory)
        journal.setPrivacy(reflections: true, agents: false)
        journal.flush()
        let document = try read(directory)
        #expect(document.days.reduce(Int64(0)) { $0 + $1.buttonPresses } == 42)
        #expect(document.days.reduce(0) { $0 + $1.voiceSeconds } == 16)
        #expect(document.reflections.first?.text == "共享记录 café：只保存本次新增的文字。")
        #expect(document.recordReflections)
        #expect(!document.agentAccessEnabled)
    }

    @Test func localMidnightAndPrivacyUseTheSharedSemantics() throws {
        let directory = try temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let journal = VoiceAnythingJournal(directory: directory)
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        let start = try #require(VoiceAnythingJournal.instant("2026-09-21T23:59:50Z"))
        let end = start.addingTimeInterval(20)
        let session = UUID()
        journal.recordVoice(start: start, end: end, audioSeconds: 16, calendar: calendar)
        journal.append(sessionID: session, start: start, end: end, application: "Editor", model: "example-device", text: "Private")
        journal.flush()
        let disabled = try read(directory)
        #expect(disabled.reflections.isEmpty)
        #expect(disabled.days.map(\.date) == ["2026-09-21", "2026-09-22"])
        #expect(disabled.days.map(\.voiceSeconds) == [8, 8])
        #expect(disabled.days.map(\.voiceSessions) == [1, 0])
        journal.setPrivacy(reflections: true, agents: false)
        journal.append(sessionID: session, start: start, end: end, application: "Editor", model: "example-device", text: "新增文字")
        journal.append(sessionID: session, start: start, end: end, application: "Editor", model: "example-device", text: "重复会话")
        journal.flush()
        #expect(try read(directory).reflections.count == 1)
    }

    @Test func corruptStorageIsNeverOverwritten() throws {
        let directory = try temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let url = directory.appendingPathComponent("journal.json")
        let original = Data("broken".utf8)
        try original.write(to: url)
        let journal = VoiceAnythingJournal(directory: directory)
        journal.recordButton(at: Date())
        journal.flush()
        #expect(try Data(contentsOf: url) == original)
        #expect(!VoiceAnythingJournal.validDate("2026-02-30"))
        #expect(VoiceAnythingJournal.validDate("2024-02-29"))
    }

    @Test func bundledHardwareAndCustomControlIDsDecodeWithoutEnumChanges() throws {
        let profile = try VADeviceProfile.load(at: repository.appendingPathComponent("devices/xiaomi-rc003"))
        #expect(profile.controls.count == 13)
        #expect(profile.capabilities.voice && profile.capabilities.holdToTalk)
        #expect(profile.controls.first { $0.id == "Microphone" }?.button == nil)
        let button = try #require(RemoteButton(rawValue: "CaptureNote"))
        #expect(try JSONDecoder().decode(RemoteButton.self, from: JSONEncoder().encode(button)) == button)
        let control = VADeviceControl(id: "CaptureNote", label: "笔记", usage: 0x123,
            x: 0.1, y: 0.1, width: 0.2, height: 0.2, gestures: ["single"])
        #expect(control.button == button)
    }
}

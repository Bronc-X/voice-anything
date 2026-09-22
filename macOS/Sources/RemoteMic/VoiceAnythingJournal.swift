import Combine
import CryptoKit
import Foundation

// This on-disk contract is shared with SayAll.Core.History and the same read-only MCP helper.
struct VADay: Codable, Identifiable {
    let date: String
    var buttonPresses: Int64 = 0
    var voiceSeconds: Double = 0
    var voiceSessions: Int64 = 0
    var id: String { date }
}
struct VAReflection: Codable, Identifiable, Equatable {
    let id: String
    let sessionId: String
    let startedAt: String
    let endedAt: String
    let application: String
    let deviceModel: String
    let text: String
    let captureMethod: String
}
struct VAAgentGrant: Codable, Identifiable {
    let id: String
    let name: String
    let tokenHash: String
    let createdAt: String
}
struct VAJournalDocument: Codable {
    var schemaVersion = 1
    var recordReflections = false
    var agentAccessEnabled = false
    var days: [VADay] = []
    var reflections: [VAReflection] = []
    var grants: [VAAgentGrant] = []
}
enum VAStorageError: LocalizedError {
    case invalid, capacity, disabled
    var errorDescription: String? {
        switch self {
        case .invalid: return "本地记录格式无效，未覆盖原文件。"
        case .capacity: return "本地记录达到容量上限，请先导出并删除不需要的回眸。"
        case .disabled: return "请先开启本地 Agent 访问。"
        }
    }
}

final class VoiceAnythingJournal: ObservableObject {
    static var defaultDirectory: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/VoiceAnything", isDirectory: true)
    }
    @Published private(set) var document = VAJournalDocument()
    @Published private(set) var newestReflections: [VAReflection] = []
    @Published private(set) var status = "所有数据保存在本机"
    let directory: URL
    private let queue = DispatchQueue(label: "VoiceAnything.journal")
    private var cachedReflections: [VAReflection] = []
    private var sortedReflections: [VAReflection] = []
    private var url: URL { directory.appendingPathComponent("journal.json") }

    init(directory: URL = VoiceAnythingJournal.defaultDirectory) {
        self.directory = directory
        reload()
    }

    static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.timeZone = .current
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
    static func dateKey(_ date: Date, calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", parts.year!, parts.month!, parts.day!)
    }
    static func validDate(_ value: String) -> Bool {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        formatter.dateFormat = "yyyy-MM-dd"
        formatter.isLenient = false
        guard let date = formatter.date(from: value) else { return false }
        return formatter.string(from: date) == value
    }
    static func instant(_ value: String) -> Date? {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = formatter.date(from: value) { return date }
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: value)
    }
    private func read() throws -> VAJournalDocument {
        guard FileManager.default.fileExists(atPath: url.path) else { return VAJournalDocument() }
        let attributes = try FileManager.default.attributesOfItem(atPath: url.path)
        guard ((attributes[.size] as? NSNumber)?.int64Value ?? 0) <= 64 * 1024 * 1024 else { throw VAStorageError.capacity }
        let value = try JSONDecoder().decode(VAJournalDocument.self, from: Data(contentsOf: url))
        guard value.schemaVersion == 1, value.reflections.count <= 10_000, value.grants.count <= 32,
              Set(value.days.map(\.date)).count == value.days.count,
              Set(value.reflections.map(\.id)).count == value.reflections.count,
              Set(value.reflections.map(\.sessionId)).count == value.reflections.count,
              Set(value.grants.map(\.id)).count == value.grants.count,
              value.days.allSatisfy({ Self.validDate($0.date) &&
                  $0.buttonPresses >= 0 && $0.voiceSeconds.isFinite && $0.voiceSeconds >= 0 && $0.voiceSessions >= 0 }),
              value.reflections.allSatisfy({ record in
                  guard let start = Self.instant(record.startedAt), let end = Self.instant(record.endedAt) else { return false }
                  return UUID(uuidString: record.id) != nil && UUID(uuidString: record.sessionId) != nil && end >= start &&
                      !record.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && record.text.utf16.count <= 32_768 &&
                      !record.application.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && record.application.utf16.count <= 256 &&
                      record.deviceModel.utf16.count <= 128 && ["accessibility-delta-v1", "manual"].contains(record.captureMethod)
              }),
              value.grants.allSatisfy({ UUID(uuidString: $0.id) != nil && Self.instant($0.createdAt) != nil &&
                  !$0.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && $0.name.utf16.count <= 80 &&
                  $0.tokenHash.range(of: #"^[0-9A-F]{64}$"#, options: .regularExpression) != nil })
        else { throw VAStorageError.invalid }
        return value
    }
    private func publish(_ value: VAJournalDocument?, error: Error? = nil) {
        if let value, value.reflections != cachedReflections {
            cachedReflections = value.reflections
            let fractional = ISO8601DateFormatter()
            fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            let whole = ISO8601DateFormatter()
            var dated: [(record: VAReflection, endedAt: Date)] = value.reflections.map { record in
                let endedAt = fractional.date(from: record.endedAt)
                    ?? whole.date(from: record.endedAt) ?? Date.distantPast
                return (record: record, endedAt: endedAt)
            }
            dated.sort { left, right in
                if left.endedAt == right.endedAt { return left.record.id < right.record.id }
                return left.endedAt > right.endedAt
            }
            sortedReflections = dated.map { $0.record }
        }
        let newest = sortedReflections
        DispatchQueue.main.async {
            if let value { self.document = value; self.newestReflections = newest }
            self.status = error.map { "未保存：\($0.localizedDescription)" } ?? "所有数据保存在本机"
        }
    }
    func reload() {
        queue.async { do { self.publish(try self.read()) } catch { self.publish(nil, error: error) } }
    }
    private func update(_ transform: @escaping (inout VAJournalDocument) throws -> Void,
                        completion: ((Error?) -> Void)? = nil) {
        queue.async {
            do {
                var value = try self.read()
                try transform(&value)
                let encoder = JSONEncoder()
                encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
                let data = try encoder.encode(value)
                guard data.count <= 64 * 1024 * 1024 else { throw VAStorageError.capacity }
                try FileManager.default.createDirectory(at: self.directory, withIntermediateDirectories: true,
                    attributes: [.posixPermissions: 0o700])
                try data.write(to: self.url, options: .atomic)
                try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: self.url.path)
                self.publish(value)
                DispatchQueue.main.async { completion?(nil) }
            } catch {
                self.publish(nil, error: error)
                DispatchQueue.main.async { completion?(error) }
            }
        }
    }
    func setPrivacy(reflections: Bool, agents: Bool, completion: ((Error?) -> Void)? = nil) {
        update({ $0.recordReflections = reflections; $0.agentAccessEnabled = agents }, completion: completion)
    }
    func recordButton(at date: Date) {
        update { value in
            let key = Self.dateKey(date)
            let index = value.days.firstIndex(where: { $0.date == key }) ?? value.days.count
            if index == value.days.count { value.days.append(VADay(date: key)) }
            guard value.days[index].buttonPresses < Int64.max else { throw VAStorageError.capacity }
            value.days[index].buttonPresses += 1
            value.days.sort { $0.date < $1.date }
        }
    }
    func recordVoice(start: Date, end: Date, audioSeconds: Double, calendar: Calendar = .current) {
        guard end >= start, audioSeconds.isFinite, audioSeconds > 0, audioSeconds <= 86_400,
              end.timeIntervalSince(start) <= 172_800 else { return }
        update { value in
            var cursor = start
            var first = true
            repeat {
                let key = Self.dateKey(cursor, calendar: calendar)
                guard let interval = calendar.dateInterval(of: .day, for: cursor) else { throw VAStorageError.invalid }
                let segmentEnd = min(interval.end, end)
                let fraction = start == end ? 1 : segmentEnd.timeIntervalSince(cursor) / end.timeIntervalSince(start)
                let index = value.days.firstIndex(where: { $0.date == key }) ?? value.days.count
                if index == value.days.count { value.days.append(VADay(date: key)) }
                let seconds = value.days[index].voiceSeconds + audioSeconds * fraction
                guard seconds.isFinite, !first || value.days[index].voiceSessions < Int64.max else { throw VAStorageError.capacity }
                value.days[index].voiceSeconds = seconds
                if first { value.days[index].voiceSessions += 1 }
                cursor = segmentEnd
                first = false
            } while cursor < end
            value.days.sort { $0.date < $1.date }
        }
    }
    func append(sessionID: UUID, start: Date, end: Date, application: String, model: String, text: String) {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, text.utf16.count <= 32_768,
              !application.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, application.utf16.count <= 256,
              model.utf16.count <= 128, end >= start else { return }
        update { value in
            guard value.recordReflections, !value.reflections.contains(where: { $0.sessionId == sessionID.uuidString }) else { return }
            guard value.reflections.count < 10_000 else { throw VAStorageError.capacity }
            value.reflections.append(VAReflection(id: UUID().uuidString, sessionId: sessionID.uuidString,
                startedAt: Self.timestamp(start), endedAt: Self.timestamp(end), application: application,
                deviceModel: model, text: text, captureMethod: "accessibility-delta-v1"))
        }
    }
    func delete(id: String) { update { $0.reflections.removeAll { $0.id == id } } }
    func revoke(id: String) { update { $0.grants.removeAll { $0.id == id } } }
    func grant(name: String, completion: @escaping (Result<String, Error>) -> Void) {
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.utf16.count <= 80 else { completion(.failure(VAStorageError.invalid)); return }
        var random = SystemRandomNumberGenerator()
        let token = (0..<32).map { _ in String(format: "%02X", UInt8.random(in: 0...255, using: &random)) }.joined()
        let hash = SHA256.hash(data: Data(token.utf8)).map { String(format: "%02X", $0) }.joined()
        update({ value in
            guard value.agentAccessEnabled else { throw VAStorageError.disabled }
            guard value.grants.count < 32 else { throw VAStorageError.capacity }
            value.grants.append(VAAgentGrant(id: UUID().uuidString, name: name, tokenHash: hash, createdAt: Self.timestamp(Date())))
        }, completion: { error in completion(error.map { .failure($0) } ?? .success(token)) })
    }
    // Called by the app's normal termination path, after stopping audio and capture.
    func flush() { queue.sync {} }
}

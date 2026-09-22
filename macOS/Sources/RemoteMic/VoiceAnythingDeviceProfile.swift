import AppKit
import Combine
import Foundation

struct VADeviceControl: Codable, Identifiable {
    let id: String
    let label: String
    let usage: UInt16
    let x: Double
    let y: Double
    let width: Double
    let height: Double
    let gestures: [String]
    var button: RemoteButton? {
        guard !gestures.contains("voice") else { return nil }
        let names = ["Power": "power", "Up": "up", "Left": "left", "Ok": "ok", "Right": "right",
                     "Down": "down", "Back": "back", "VolumeUp": "volume_up", "Home": "home",
                     "VolumeDown": "volume_down", "Menu": "menu", "Tv": "tv"]
        return RemoteButton(rawValue: names[id] ?? id)
    }
}
struct VADeviceCapabilities: Codable {
    let voice: Bool
    let holdToTalk: Bool
    let toggleVoice: Bool
    let battery: Bool
    let touch: Bool
}
struct VADeviceTransport: Codable {
    let vendorId: UInt16
    let productId: UInt16
    let productVersion: UInt16
    let vendorIdSource: UInt8
    let advertisedNames: [String]
}
struct VADeviceProfile: Codable, Identifiable {
    let schemaVersion: Int
    let id: String
    let name: String
    let adapter: String
    let modelNumbers: [String]
    let artwork: String?
    let aspectRatio: Double
    let capabilities: VADeviceCapabilities
    let controls: [VADeviceControl]
    let validation: [String: String]
    let transport: VADeviceTransport?

    func matchesModel(_ raw: String) -> Bool {
        let model = raw.trimmingCharacters(in: .whitespacesAndNewlines.union(.controlCharacters))
        return modelNumbers.contains { $0.caseInsensitiveCompare(model) == .orderedSame }
    }

    func validate() throws {
        func matches(_ text: String, _ regex: String) -> Bool { text.range(of: regex, options: .regularExpression) != nil }
        guard schemaVersion == 1, matches(id, "^[a-z0-9][a-z0-9-]{2,63}$"), !name.isEmpty, name.utf16.count <= 80,
              adapter == "xiaomi-atvv-v1", (1...16).contains(modelNumbers.count),
              modelNumbers.allSatisfy({ !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && $0.utf16.count <= 64 }),
              aspectRatio.isFinite, (0.1...3).contains(aspectRatio), (1...64).contains(controls.count),
              !capabilities.toggleVoice, !capabilities.touch, !capabilities.battery,
              capabilities.voice == controls.contains(where: { $0.gestures.contains("voice") }),
              capabilities.voice == capabilities.holdToTalk,
              Set(controls.map(\.id)).count == controls.count, Set(controls.map(\.usage)).count == controls.count,
              validation.allSatisfy({ ["windows", "macos"].contains($0.key) && ["candidate", "research", "verified"].contains($0.value) }),
              controls.allSatisfy({ control in
                  matches(control.id, "^[A-Za-z][A-Za-z0-9]{0,47}$") && !control.label.isEmpty && control.label.utf16.count <= 32 && control.usage > 0 &&
                  [control.x, control.y, control.width, control.height].allSatisfy(\.isFinite) &&
                  control.x >= 0 && control.y >= 0 && control.width > 0 && control.height > 0 &&
                  control.x + control.width <= 1 && control.y + control.height <= 1 &&
                  (1...3).contains(control.gestures.count) && Set(control.gestures).count == control.gestures.count &&
                  control.gestures.allSatisfy({ ["single", "double", "long", "voice"].contains($0) }) &&
                  (control.id == "Microphone" ? control.gestures == ["voice"] : !control.gestures.contains("voice"))
              }) else { throw VAStorageError.invalid }
        if let transport {
            guard transport.vendorId > 0, transport.productId > 0, [1, 2].contains(transport.vendorIdSource),
                  (1...16).contains(transport.advertisedNames.count), transport.advertisedNames.allSatisfy({
                      !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && $0.utf16.count <= 80
                  }) else { throw VAStorageError.invalid }
        }
    }
    func artworkURL(in directory: URL) throws -> URL? {
        guard let artwork else { return nil }
        let pieces = artwork.components(separatedBy: "/")
        guard !artwork.contains(":"), !artwork.contains("\\"), !pieces.contains(where: { $0 == ".." || $0 == "." || $0.isEmpty }),
              ["png", "jpg", "jpeg"].contains((artwork as NSString).pathExtension.lowercased()) else { throw VAStorageError.invalid }
        let url = directory.appendingPathComponent(artwork).standardizedFileURL
        guard url.path.hasPrefix(directory.standardizedFileURL.path + "/") else { throw VAStorageError.invalid }
        return url
    }
    static func load(at directory: URL) throws -> VADeviceProfile {
        let url = directory.appendingPathComponent("profile.json")
        guard try fileSize(url) <= 128 * 1024 else { throw VAStorageError.capacity }
        let data = try Data(contentsOf: url)
        // Reject misspelled fields just as the Windows parser does.
        let allowed = Set(["schemaVersion", "id", "name", "adapter", "modelNumbers", "artwork", "aspectRatio", "capabilities", "controls", "validation", "transport"])
        guard let object = try JSONSerialization.jsonObject(with: data) as? [String: Any], Set(object.keys).isSubset(of: allowed),
              let capabilities = object["capabilities"] as? [String: Any],
              Set(capabilities.keys).isSubset(of: ["voice", "holdToTalk", "toggleVoice", "battery", "touch"]),
              let controls = object["controls"] as? [[String: Any]],
              controls.allSatisfy({ Set($0.keys).isSubset(of: ["id", "label", "usage", "x", "y", "width", "height", "gestures"]) })
        else { throw VAStorageError.invalid }
        if let transport = object["transport"] as? [String: Any],
           !Set(transport.keys).isSubset(of: ["vendorId", "productId", "productVersion", "vendorIdSource", "advertisedNames"]) {
            throw VAStorageError.invalid
        }
        let profile = try JSONDecoder().decode(VADeviceProfile.self, from: data)
        try profile.validate()
        if let image = try profile.artworkURL(in: directory) {
            guard try fileSize(image) <= 8 * 1024 * 1024, NSImage(contentsOf: image) != nil else { throw VAStorageError.invalid }
        }
        return profile
    }
    private static func fileSize(_ url: URL) throws -> Int64 {
        (try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.int64Value ?? 0
    }
}
struct VAInstalledProfile: Identifiable {
    let profile: VADeviceProfile
    let directory: URL
    var id: String { profile.id }
    var usageMap: [UInt16: RemoteButton] {
        Dictionary(uniqueKeysWithValues: profile.controls.compactMap { control in control.button.map { (control.usage, $0) } })
    }
}

final class VoiceAnythingDevices: ObservableObject {
    static let shared = VoiceAnythingDevices()
    // Kept off in upstream unit tests; the application explicitly enables this integration.
    static var enabled = false
    @Published private(set) var profiles: [VAInstalledProfile] = []
    @Published private(set) var status = ""
    private var bindings: [String: String] { (UserDefaults.standard.dictionary(forKey: "VoiceAnything.deviceProfiles") as? [String: String]) ?? [:] }
    private var identifiedModels: [UUID: String] = [:]
    private var userDirectory: URL { VoiceAnythingJournal.defaultDirectory.appendingPathComponent("devices", isDirectory: true) }
    private init() { reload() }
    func reload() {
        do {
            var values: [VAInstalledProfile] = []
            for directory in [Bundle.main.resourceURL?.appendingPathComponent("devices"), userDirectory].compactMap({ $0 }) {
                guard FileManager.default.fileExists(atPath: directory.path) else { continue }
                for entry in try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) {
                    values.append(VAInstalledProfile(profile: try VADeviceProfile.load(at: entry), directory: entry))
                }
            }
            guard values.count <= 100, Set(values.map(\.id)).count == values.count else { throw VAStorageError.invalid }
            profiles = values.sorted { $0.id < $1.id }
            status = "型号配置已载入。导入配置不代表已通过真机验收。"
        } catch { status = "型号载入失败：\(error.localizedDescription)" }
    }
    func profile(for deviceID: UUID?) -> VAInstalledProfile? {
        guard Self.enabled, let deviceID, let id = bindings[deviceID.uuidString] else { return nil }
        return profiles.first { $0.id == id }
    }
    func matchingModel(_ raw: String) -> VAInstalledProfile? {
        guard Self.enabled else { return nil }
        let matches = profiles.filter { $0.profile.matchesModel(raw) }
        return matches.count == 1 ? matches[0] : nil
    }
    func recognizesAdvertisedName(_ raw: String?) -> Bool {
        guard Self.enabled, let raw else { return false }
        let name = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        return profiles.filter { item in item.profile.transport?.advertisedNames.contains {
            $0.caseInsensitiveCompare(name) == .orderedSame
        } == true }.count == 1
    }
    func didIdentify(model: String, deviceID: UUID, settings: AppSettings) {
        identifiedModels[deviceID] = model
        guard let profile = matchingModel(model) else { return }
        bindMatchedHID(profile, to: deviceID, settings: settings)
    }
    var hidMatching: [[String: Int]] {
        guard Self.enabled else { return [] }
        return profiles.compactMap { item in item.profile.transport.map {
            ["VendorID": Int($0.vendorId), "ProductID": Int($0.productId), "VersionNumber": Int($0.productVersion)]
        } }
    }
    func matchingHID(vendor: Int, product: Int, version: Int) -> VAInstalledProfile? {
        guard Self.enabled else { return nil }
        let matches = profiles.filter { item in
            guard let value = item.profile.transport else { return false }
            return Int(value.vendorId) == vendor && Int(value.productId) == product && Int(value.productVersion) == version
        }
        return matches.count == 1 ? matches[0] : nil
    }
    func bindMatchedHID(_ profile: VAInstalledProfile, to deviceID: UUID, settings: AppSettings) {
        var values = bindings
        let changed = values[deviceID.uuidString] != profile.id
        values[deviceID.uuidString] = profile.id
        UserDefaults.standard.set(values, forKey: "VoiceAnything.deviceProfiles")
        if changed && profile.id != "xiaomi-rc003" {
            let previous = settings.selectedRemoteProfileID
            settings.selectRemoteProfile(deviceID)
            for control in profile.profile.controls {
                guard let button = control.button else { continue }
                for trigger in [ButtonTrigger.singleClick, .doubleClick, .longPress] {
                    settings.setAction(.disabled, for: button, trigger: trigger)
                }
            }
            if let previous { settings.selectRemoteProfile(previous) }
        }
        objectWillChange.send()
    }
    func bind(_ profile: VAInstalledProfile, to device: RemoteDeviceProfile) throws {
        let models = VoiceRemoteCatalog.entries.first(where: { $0.model == device.model })?.disModelNumbers ?? []
        let observedModel = identifiedModels[device.id]
        guard observedModel.map(profile.profile.matchesModel) ?? profile.profile.modelNumbers.contains(where: { wanted in models.contains { $0.caseInsensitiveCompare(wanted) == .orderedSame } })
        else { throw VAStorageError.invalid }
        var values = bindings
        values[device.id.uuidString] = profile.id
        UserDefaults.standard.set(values, forKey: "VoiceAnything.deviceProfiles")
        objectWillChange.send()
    }
    func importProfile(from source: URL) throws {
        let profile = try VADeviceProfile.load(at: source)
        guard !profiles.contains(where: { $0.id == profile.id }) else { throw VAStorageError.invalid }
        try FileManager.default.createDirectory(at: userDirectory, withIntermediateDirectories: true)
        let staging = userDirectory.appendingPathComponent(".import-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: false)
        defer { if FileManager.default.fileExists(atPath: staging.path) { try? FileManager.default.removeItem(at: staging) } }
        try FileManager.default.copyItem(at: source.appendingPathComponent("profile.json"), to: staging.appendingPathComponent("profile.json"))
        if let image = try profile.artworkURL(in: source), let target = try profile.artworkURL(in: staging) {
            try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
            try FileManager.default.copyItem(at: image, to: target)
        }
        _ = try VADeviceProfile.load(at: staging)
        try FileManager.default.moveItem(at: staging, to: userDirectory.appendingPathComponent(profile.id))
        reload()
    }
}

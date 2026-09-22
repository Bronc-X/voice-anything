import Foundation

enum AppLinks {
    static let githubRepository = URL(
        string: "https://github.com/Bronc-X/voice-anything"
    )!
    static let chineseWebsite = URL(string: "https://github.com/Bronc-X/voice-anything")!
    static let englishWebsite = URL(string: "https://github.com/Bronc-X/voice-anything")!
    static let testFlightPublicBeta = URL(
        string: "https://github.com/Bronc-X/voice-anything"
    )!
    static let feedback = URL(
        string: "https://github.com/Bronc-X/voice-anything/issues"
    )!
    static let doubaoInputMethod = URL(
        string: "https://shurufa.doubao.com/"
    )!

    static func website(for locale: Locale) -> URL {
        locale.identifier.lowercased().hasPrefix("zh")
            ? chineseWebsite
            : englishWebsite
    }
}

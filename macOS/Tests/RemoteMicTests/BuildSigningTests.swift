import AppKit
import Foundation
import Testing

// The upstream commercial release pipeline is not distributed by this fork.
// Its source-string assertions are replaced by checks of this product's inputs;
// CI also builds the real bundle and runs verify-app.sh against the result.
@Suite("Voice Anything packaging")
struct BuildSigningTests {
    private var root: URL {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
    }
    @Test func bundleHasIndependentIdentityAndNoInheritedUpdateFeed() throws {
        let data = try Data(contentsOf: root.appendingPathComponent("Resources/Info.plist"))
        let plist = try #require(PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any])
        #expect(plist["CFBundleIdentifier"] as? String == "io.github.bronc-x.voice-anything")
        #expect(plist["CFBundleDisplayName"] as? String == "Voice Anything")
        #expect(plist["SUFeedURL"] == nil)
        #expect(plist["SUPublicEDKey"] == nil)
        #expect(plist["SUEnableAutomaticChecks"] as? Bool == false)
        #expect(plist["VAHardwareAnnouncementsEnabled"] as? Bool == false)
    }
    @Test func applicationIconIsDecodableAndHasUsefulResolution() throws {
        let image = try #require(NSBitmapImageRep(data: Data(contentsOf: root.appendingPathComponent("Resources/AppIcon.png"))))
        #expect(image.pixelsWide >= 512)
        #expect(image.pixelsWide == image.pixelsHigh)
    }
    @Test func nativeLocalizationAndSetupResourcesArePresent() throws {
        for language in ["en", "zh-Hans"] {
            let url = root.appendingPathComponent("Resources/\(language).lproj/Localizable.strings")
            let values = try #require(NSDictionary(contentsOf: url) as? [String: String])
            #expect(values["app.name"] == "Voice Anything")
        }
        let names = ["doubao-menu", "doubao-settings", "system-fn", "weixin-app-shortcuts", "weixin-input-menu", "weixin-input-settings"]
        for name in names {
            for appearance in ["light", "dark"] {
                let url = root.appendingPathComponent("Resources/Onboarding/\(name)-\(appearance).png")
                #expect(NSImage(contentsOf: url) != nil)
            }
        }
    }
}

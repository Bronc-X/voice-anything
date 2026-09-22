#!/usr/bin/env bash
set -euo pipefail

mac_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_root="$(cd "$mac_root/.." && pwd)"
cd "$mac_root"
case "$(uname -m)" in
  arm64) runtime=osx-arm64 ;;
  x86_64) runtime=osx-x64 ;;
  *) echo 'Unsupported macOS architecture' >&2; exit 1 ;;
esac

swift build -c release --product RemoteMic
binary_dir="$(swift build -c release --show-bin-path)"
mkdir -p "$repo_root/artifacts"
output="$(mktemp -d "$repo_root/artifacts/VoiceAnything-$runtime-XXXXXX")"
app="$output/Voice Anything.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$app/Contents/Helpers" "$app/Contents/Frameworks"
cp "$binary_dir/RemoteMic" "$app/Contents/MacOS/RemoteMic"
cp "$mac_root/Resources/Info.plist" "$app/Contents/Info.plist"
cp "$mac_root/Resources/"*.png "$app/Contents/Resources/"
cp -R "$mac_root/Resources/en.lproj" "$mac_root/Resources/zh-Hans.lproj" "$mac_root/Resources/Onboarding" "$mac_root/Resources/CommonPhrases" "$app/Contents/Resources/"
cp -R "$repo_root/devices" "$app/Contents/Resources/devices"
cp "$repo_root/LICENSE.md" "$repo_root/THIRD_PARTY_NOTICES.md" "$app/Contents/Resources/"
cp -R "$repo_root/docs/licenses" "$app/Contents/Resources/licenses"

framework="$(find .build/artifacts -type d -name Sparkle.framework -print -quit)"
if [[ -z "$framework" ]]; then echo 'Sparkle framework was not produced by SwiftPM' >&2; exit 1; fi
ditto "$framework" "$app/Contents/Frameworks/Sparkle.framework"
if ! otool -l "$app/Contents/MacOS/RemoteMic" | grep -q '@executable_path/../Frameworks'; then
  install_name_tool -add_rpath '@executable_path/../Frameworks' "$app/Contents/MacOS/RemoteMic"
fi

iconset="$output/VoiceAnything.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$mac_root/Resources/AppIcon.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$mac_root/Resources/AppIcon.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
dotnet publish "$repo_root/Windows/src/VoiceAnything.Mcp/VoiceAnything.Mcp.csproj" \
  -c Release -r "$runtime" --self-contained true -p:PublishSingleFile=true \
  -o "$app/Contents/Helpers"

# Sign nested executables before their containers. Use a stable designated
# requirement so local development rebuilds keep the same permission identity.
sparkle="$app/Contents/Frameworks/Sparkle.framework/Versions/B"
codesign --force --timestamp=none --sign - "$app/Contents/Helpers/VoiceAnything.Mcp"
codesign --force --timestamp=none --sign - "$sparkle/XPCServices/Installer.xpc"
codesign --force --timestamp=none --preserve-metadata=entitlements --sign - "$sparkle/XPCServices/Downloader.xpc"
codesign --force --timestamp=none --sign - "$sparkle/Autoupdate"
codesign --force --timestamp=none --sign - "$sparkle/Updater.app"
codesign --force --timestamp=none --sign - "$app/Contents/Frameworks/Sparkle.framework"
codesign --force --timestamp=none --sign - --requirements '=designated => identifier "io.github.bronc-x.voice-anything"' "$app"
bash "$mac_root/scripts/verify-app.sh" "$app"
printf '%s\n' "$app" > "$repo_root/artifacts/macos-app-path.txt"
printf 'Development app: %s\n' "$app"

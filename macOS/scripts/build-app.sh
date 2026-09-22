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
cp -R "$repo_root/devices" "$app/Contents/Resources/devices"
cp "$repo_root/LICENSE.md" "$repo_root/THIRD_PARTY_NOTICES.md" "$app/Contents/Resources/"
cp -R "$repo_root/docs/licenses" "$app/Contents/Resources/licenses"

framework="$(find .build/artifacts -type d -name Sparkle.framework -print -quit)"
if [[ -z "$framework" ]]; then echo 'Sparkle framework was not produced by SwiftPM' >&2; exit 1; fi
ditto "$framework" "$app/Contents/Frameworks/Sparkle.framework"
install_name_tool -add_rpath '@executable_path/../Frameworks' "$app/Contents/MacOS/RemoteMic"

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

# Local development signature only. A public release still needs signing, notarization and hardware acceptance.
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"
printf 'Development app: %s\n' "$app"

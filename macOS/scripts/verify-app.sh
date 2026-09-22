#!/usr/bin/env bash
set -euo pipefail
app="${1:?Usage: verify-app.sh '/path/to/Voice Anything.app'}"
codesign --verify --deep --strict "$app"
test -x "$app/Contents/MacOS/RemoteMic"
test -x "$app/Contents/Helpers/VoiceAnything.Mcp"
test -s "$app/Contents/Resources/AppIcon.icns"
test -s "$app/Contents/Resources/devices/xiaomi-rc003/profile.json"
test -s "$app/Contents/Resources/licenses/SPARKLE.txt"
test -s "$app/Contents/Resources/LICENSE.md"
test -d "$app/Contents/Resources/Onboarding"
for language in en zh-Hans; do
  plutil -lint "$app/Contents/Resources/$language.lproj/Localizable.strings"
done
test "$(plutil -extract CFBundleIdentifier raw -o - "$app/Contents/Info.plist")" = 'io.github.bronc-x.voice-anything'
if plutil -extract SUFeedURL raw -o - "$app/Contents/Info.plist" 2>/dev/null; then
  echo 'A release update feed must not be inherited from the upstream product.' >&2
  exit 1
fi
printf 'PASS native app: signature, identity, resources and executable MCP helper\n'

# VoiceAnything Windows regression baseline

Run from the `Windows` directory:

```powershell
.\scripts\test-baseline.ps1
```

The command exits non-zero if a protected behavior regresses. It protects:

- onboarding gates that require a real connected RC003MS remote and real input;
- the measured RC003MS HID report shape and 12-button mapping;
- exact physical-device, signed HidOverGatt driver, capture RVA, machine-code,
  pinned Gadget, authenticated IPC, and foreground-target identity gates;
- the current Windows 11 HidOverGatt driver fingerprint allowlist, including the
  verified Microsoft driver accepted on 2026-09-10;
- loopback-only Gadget transport, per-session tokens, and fail-closed rejection of
  changed binaries, offsets, machine code, hosts, devices, and messages;
- bounded five-second bridge startup recovery with one controlled HID restart;
- a fixed machine-wide service log so opening the app never starts an elevated helper;
- the observed ATVV capability/control protocol, ADPCM decoding, segmented audio
  continuation, PCM level evidence, and valid mono 16-bit WAV generation;
- the Windows 11 CsWinRT `AudioFrame` PCM buffer path using the official
  `IMemoryBufferByteAccess` interface identity;
- in-process playback of the saved recording before button configuration can continue;
- normalization of WinRT device paths to MMDevice endpoint IDs before temporarily
  routing `CABLE Output` as the Windows default microphone, followed by restoration;
- a fixed, user-visible catalog of Typeless, Codex voice dictation, WeChat voice input,
  and NetEase BageShuo, with install/running readiness and per-target self-test states;
- one consistent physical interaction: hold the remote microphone key to speak and
  release it to stop;
- one complete Left Alt tap on physical press and another on physical release for
  Typeless, plus the same toggle plan with Right Alt for NetEase BageShuo;
- held `Ctrl+Shift+D` for Codex and held `Left Ctrl+Left Windows` for WeChat, released
  only by the physical microphone-key release rather than an ATVV segment boundary;
- Typeless as the default and persisted voice target without hiding the other three
  explicit choices;
- user-visible start/stop guidance for both tap-to-toggle and hold-shortcut targets;
- bounded rendering of up to 192 real spectrum frames so high-rate PCM remains
  responsive without reducing a long recording to only a few visible slices;
- Codex/Claude Code presets, per-button single/double/long bindings, gesture
  de-duplication, settings persistence, and enumeration of configured Windows input
  methods;
- compilation of the WPF app, background service, and one-time elevated installer.

## Passing evidence

- On 2026-08-26, Windows reported the RC003MS connected, the helper emitted real
  report-ID 1 payloads, and the app recognized a direction-button press.
- On 2026-08-29, a packaged build held Codex dictation for a 12.98-second session while
  the physical HID report stayed pressed for 12.64 seconds. Releasing the key ended
  dictation and produced a 389,324-byte local PCM WAV file.
- On 2026-09-10, the service reached
  `runtime_verified -> gadget_injected -> gadget_connected -> hook_ready` with the
  current signed Microsoft HidOverGatt driver and pinned Gadget hash
  `6FCA4007B2284C765A6C15C967A741F536B5865BF83867326A54029A3B752748`.
- On 2026-09-10, a real approximately 3.7-second remote session through Typeless
  produced a 3.0385-second OGG recording with mean volume -32.1 dB and maximum volume
  -0.1 dB. This is non-silent audio evidence, not merely a non-zero sample count.
- On 2026-09-10, all core, HID bridge, Windows integration, app, helper, and installer
  baseline steps passed with zero warnings and zero errors.

End-to-end listening, third-party transcription accuracy, and physical button presses
remain manual gates because automation cannot hear the speaker or press the remote.
Automated passing evidence must not be described as complete real-device acceptance.

## Protection and reuse

Future changes to HID, Bluetooth, onboarding, ATVV, voice-input selection or dispatch,
button mapping, UI state, service setup, or packaging must reuse this command before and
after the change. Do not replace it with a narrower one-off script. The installer is
produced independently with:

```powershell
.\scripts\publish-installer.ps1 -DotNetPath <path-to-dotnet.exe>
```

The shortest reproducible implementation path and evidence chain remain recorded in
`Testing/RC003MS_GOLDEN_PATH_AND_TYPELESS.md`.

Git checkpoint when this baseline was extended: branch
`feature/windows-rc003ms-port`, source commit `f54e6fa`. Suggested tag after the
Windows changes are committed:
`baseline-2026-09-10-vibecontrol-multi-input`.

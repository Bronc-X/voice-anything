import AppKit
import SwiftUI

struct VoiceAnythingDevicesView: View {
    @ObservedObject var settings: AppSettings
    @ObservedObject private var devices = VoiceAnythingDevices.shared
    @EnvironmentObject private var localization: LocalizationStore
    @State private var selectedID = ""
    @State private var controlID = ""
    @State private var gesture = "single"
    @State private var message = ""
    @State private var editingShortcut = false

    private var selected: VAInstalledProfile? { devices.profiles.first { $0.id == selectedID } }
    private var control: VADeviceControl? { selected?.profile.controls.first { $0.id == controlID } }
    private var trigger: ButtonTrigger { gesture == "double" ? .doubleClick : gesture == "long" ? .longPress : .singleClick }
    private var isBound: Bool { devices.profile(for: settings.selectedRemoteProfileID)?.id == selectedID }
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            HStack {
                VStack(alignment: .leading, spacing: 6) {
                    Text("设备与型号").font(.system(size: 28, weight: .semibold))
                    Text("选好型号，就能看到它的外形、按键和可用手势。").foregroundStyle(.secondary)
                }
                Spacer()
                Button("导入型号包") { importProfile() }
            }
            Picker("型号", selection: $selectedID) {
                Text("选择型号").tag("")
                ForEach(devices.profiles) { Text($0.profile.name).tag($0.id) }
            }
            HStack {
                Text(settings.selectedRemoteProfile == nil ? "先到连接页配对并选择一台设备。" : "把这个型号的布局用于连接页选中的设备。")
                    .foregroundStyle(.secondary)
                Spacer()
                Button(isBound ? "已应用" : "应用到当前设备") {
                    guard let selected, let device = settings.selectedRemoteProfile else { return }
                    do { try devices.bind(selected, to: device); message = "已应用。更改按键布局后，请重新连接设备。" }
                    catch { message = "所选型号与设备报告的型号不符，原配置已保留。" }
                }.disabled(settings.selectedRemoteProfile == nil || selected == nil || isBound)
            }
            if let selected {
                HStack(alignment: .top, spacing: 30) {
                    deviceCanvas(selected).frame(width: 250, height: 430)
                    VStack(alignment: .leading, spacing: 16) {
                        Text(control?.label ?? "选择一个按键").font(.title2.weight(.semibold))
                        Text("\(selected.profile.controls.count) 个控件 · \(selected.profile.capabilities.voice ? "按住说话" : "仅按键")")
                            .foregroundStyle(.secondary)
                        if let control {
                            if control.gestures.contains("voice") {
                                Text("按住开始语音，松开结束。输入法和音频设备在连接页设置。")
                            } else if let button = control.button {
                                Picker("手势", selection: $gesture) {
                                    ForEach(control.gestures, id: \.self) { Text(gestureName($0)).tag($0) }
                                }.pickerStyle(.segmented)
                                Picker("动作", selection: Binding(
                                    get: { settings.configuredAction(for: button, trigger: trigger).action },
                                    set: { settings.setAction($0, for: button, trigger: trigger); editingShortcut = $0 == .customShortcut })) {
                                    ForEach(ButtonAction.pickerActions(installedBundleIdentifiers: PresetApplication.installedBundleIdentifiers,
                                        current: settings.configuredAction(for: button, trigger: trigger).action,
                                        experimentalContinuousRecordingEnabled: false).filter { $0 != .openCustomApplication }, id: \.self) { action in
                                        Text(action.displayName(using: localization)).tag(action)
                                    }
                                }.disabled(!isBound)
                                if settings.configuredAction(for: button, trigger: trigger).action == .customShortcut {
                                    Button("编辑快捷键") { editingShortcut = true }.disabled(!isBound)
                                }
                                Text(isBound ? "修改自动保存到当前设备。" : "先应用到当前设备，再配置按键动作。")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                        }
                        Spacer()
                        Text("macOS：\(selected.profile.validation["macos"] ?? "research")")
                            .font(.caption).foregroundStyle(.secondary)
                        Text("导入型号包后，仍需真机验证。新的传输方式需要另写适配器。")
                            .font(.caption).foregroundStyle(.secondary)
                    }.frame(maxWidth: .infinity, alignment: .leading)
                }
            } else {
                Text("选择或导入型号后，这里会显示它的外形与可配置按键。")
                    .foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            Spacer(minLength: 0)
            Text(message.isEmpty ? devices.status : message).font(.caption).foregroundStyle(.secondary)
        }.padding(28)
        .onAppear { selectedID = devices.profile(for: settings.selectedRemoteProfileID)?.id ?? devices.profiles.first?.id ?? "" }
        .onChange(of: selectedID) { _, _ in
            let first = selected?.profile.controls.first { $0.button != nil } ?? selected?.profile.controls.first
            controlID = first?.id ?? ""
            gesture = first?.gestures.first ?? "single"
        }
        .onChange(of: settings.selectedRemoteProfileID) { _, current in
            if let profile = devices.profile(for: current) { selectedID = profile.id }
        }
        .sheet(isPresented: $editingShortcut) {
            VStack(alignment: .leading, spacing: 18) {
                Text("设置快捷键").font(.title2)
                if let button = control?.button {
                    KeyboardShortcutPicker(shortcut: settings.configuredAction(for: button, trigger: trigger).shortcut) { shortcut in
                        settings.setShortcut(shortcut, for: button, trigger: trigger)
                        editingShortcut = false
                    }
                }
                Button("关闭") { editingShortcut = false }
            }.padding(24).frame(width: 760, height: 570)
        }
    }
    private func deviceCanvas(_ installed: VAInstalledProfile) -> some View {
        let profile = installed.profile
        let width = min(250.0, 420 * profile.aspectRatio)
        let height = width / profile.aspectRatio
        return ZStack(alignment: .topLeading) {
            if let url = try? profile.artworkURL(in: installed.directory), let image = NSImage(contentsOf: url) {
                Image(nsImage: image).resizable().frame(width: width, height: height)
            } else {
                RoundedRectangle(cornerRadius: 24).fill(Color.secondary.opacity(0.1)).frame(width: width, height: height)
            }
            ForEach(profile.controls) { control in
                Button {
                    controlID = control.id
                    gesture = control.gestures.first ?? "single"
                } label: {
                    ZStack {
                        RoundedRectangle(cornerRadius: 7).fill(controlID == control.id ? Color.accentColor.opacity(0.35) : Color.clear)
                        RoundedRectangle(cornerRadius: 7).stroke(controlID == control.id ? Color.accentColor : Color.clear, lineWidth: 2)
                        if profile.artwork == nil { Text(control.label).font(.caption2) }
                    }
                }.buttonStyle(.plain).help(control.label).accessibilityLabel(control.label)
                    .frame(width: control.width * width, height: control.height * height)
                    .offset(x: control.x * width, y: control.y * height)
            }
        }.frame(width: width, height: height)
    }
    private func gestureName(_ value: String) -> String { value == "single" ? "单击" : value == "double" ? "双击" : "长按" }
    private func importProfile() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false; panel.canChooseDirectories = true; panel.allowsMultipleSelection = false
        panel.message = "选择包含 profile.json 的型号包文件夹"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { selectedID = try devices.importProfile(from: url); message = "型号包已导入。" }
        catch { message = "导入失败：\(error.localizedDescription)" }
    }
}

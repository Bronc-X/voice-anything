import AppKit
import Combine
import SayAllMacRemoteCore

/// Presentation only: a connection is not proof that audio or input permissions are ready.
enum StatusItemPresentation: String, Equatable {
    case disconnected
    case connected
    case streaming

    var descriptionKey: String { "status_item.state.\(rawValue)" }

    static func resolve(
        physicalConnected: Bool,
        phoneConnected: Bool,
        watchConnected: Bool,
        webState: WebRemoteSessionState,
        isStreaming: Bool
    ) -> Self {
        if isStreaming { return .streaming }
        if physicalConnected || phoneConnected || watchConnected { return .connected }
        if case .connected = webState { return .connected }
        return .disconnected
    }

    static func publisher(
        physicalConnected: AnyPublisher<Bool, Never>,
        phoneConnected: AnyPublisher<Bool, Never>,
        watchConnected: AnyPublisher<Bool, Never>,
        webState: AnyPublisher<WebRemoteSessionState, Never>,
        isStreaming: AnyPublisher<Bool, Never>
    ) -> AnyPublisher<Self, Never> {
        Publishers.CombineLatest4(
            physicalConnected, phoneConnected, watchConnected, webState
        )
        .combineLatest(isStreaming)
        .map { connections, streaming in
            resolve(
                physicalConnected: connections.0,
                phoneConnected: connections.1,
                watchConnected: connections.2,
                webState: connections.3,
                isStreaming: streaming
            )
        }
        .removeDuplicates()
        .eraseToAnyPublisher()
    }

    @MainActor
    func apply(to button: NSStatusBarButton, description: String) {
        let resourceName = self == .streaming ? "StatusIconActiveTemplate" : "StatusIconTemplate"
        let fallbackSymbol = self == .streaming ? "mic.fill" : "dot.radiowaves.left.and.right"
        // Named images are shared. Keep localized descriptions local to this status item.
        let image = (NSImage(named: NSImage.Name(resourceName))?.copy() as? NSImage)
            ?? NSImage(systemSymbolName: fallbackSymbol, accessibilityDescription: description)
        image?.isTemplate = true
        image?.size = NSSize(width: 18, height: 18)
        image?.accessibilityDescription = description
        button.image = image
        button.title = image == nil ? description : ""
        button.appearsDisabled = self == .disconnected
        button.toolTip = description
        button.setAccessibilityLabel(description)
    }
}

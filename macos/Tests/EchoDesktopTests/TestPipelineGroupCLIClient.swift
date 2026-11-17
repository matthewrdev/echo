import Foundation

@testable import EchoDesktop

final class TestPipelineGroupCLIClient: CLIClient {
    var enabledImages = 2
    private(set) var starts: [[String]] = []

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        let command = Array(arguments.dropFirst(2))
        if command.prefix(2) == ["run", "start"] {
            starts.append(command)
            return Data()
        }
        if command == ["run", "list"] {
            return Data("[]".utf8)
        }
        if command.first == "inspect" {
            let photos = (0..<2).map { index -> [String: Any] in
                [
                    "id": "photo-\(index)", "name": "Photo \(index + 1)", "source": "photo.jpg",
                    "preview": "preview.jpg", "width": 1024, "height": 768, "camera": "Camera",
                    "focalPixels": 800, "enabled": index < enabledImages, "sets": [], "metadata": [:],
                ]
            }
            return try JSONSerialization.data(withJSONObject: [
                "projectRoot": arguments[1],
                "project": [
                    "id": "group-test", "name": "Group test", "revision": 1, "images": photos,
                    "settings": [:], "hints": [],
                ],
            ])
        }
        throw CLIError(message: "Unexpected test command: \(command)")
    }
}

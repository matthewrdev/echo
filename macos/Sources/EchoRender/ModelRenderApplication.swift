import EchoRendering
import Foundation

/// Internal, windowless entry point called by the C# render command.
final class ModelRenderApplication {
    func run() -> Int32 {
        do {
            let data = FileHandle.standardInput.readDataToEndOfFile()
            let request = try JSONDecoder().decode(ModelRenderRequest.self, from: data)
            let result = try ModelImageRenderer().render(request)
            let response = try JSONEncoder().encode(result)
            FileHandle.standardOutput.write(response)
            FileHandle.standardOutput.write(Data([10]))
            return 0
        } catch {
            FileHandle.standardError.write(Data((error.localizedDescription + "\n").utf8))
            return 5
        }
    }
}

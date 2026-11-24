import EchoRendering
import Foundation

/// Internal, windowless entry point used by the C# CLI.
final class ModelRenderApplication {
    func run(arguments: [String]) -> Int32 {
        do {
            let data = FileHandle.standardInput.readDataToEndOfFile()
            let decoder = JSONDecoder()
            let encoder = JSONEncoder()
            let response: Data
            switch arguments {
            case []:
                let request = try decoder.decode(ModelRenderRequest.self, from: data)
                response = try encoder.encode(ModelImageRenderer().render(request))
            case ["geographic-raster"]:
                let request = try decoder.decode(GeographicRasterRequest.self, from: data)
                response = try encoder.encode(request.render())
            case ["geographic-probe"]:
                let request = try decoder.decode(GeographicProbeRequest.self, from: data)
                response = try encoder.encode(request.resolve())
            case ["drone-scan"]:
                let request = try decoder.decode(DroneScanRequest.self, from: data)
                response = try encoder.encode(request.scan())
            default:
                throw ModelRenderError(message: "Unknown native tool operation.")
            }
            FileHandle.standardOutput.write(response)
            FileHandle.standardOutput.write(Data([10]))
            return 0
        } catch {
            FileHandle.standardError.write(Data((error.localizedDescription + "\n").utf8))
            return 5
        }
    }
}

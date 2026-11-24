import CryptoKit
import EchoDrone
import Foundation

struct DroneImportService {
    private let cli: CLIClient
    let reader: DroneSourceReader
    private let grouping: DroneImportGrouping
    private var fileManager: FileManager { .default }

    init(cli: CLIClient, reader: DroneSourceReader, grouping: DroneImportGrouping) {
        self.cli = cli
        self.reader = reader
        self.grouping = grouping
    }

    func suggest(_ photos: [DroneImportPhoto]) -> DroneImportSuggestions {
        grouping.suggest(photos)
    }

    func createProject(
        group: DroneImportGroup, photos: [DroneImportPhoto], source: DroneSource, destination: URL,
        progress: @escaping @Sendable (String) -> Void
    ) async throws -> DroneImportProject {
        let name = group.name.trimmingCharacters(in: .whitespacesAndNewlines)
        let target = destination.appendingPathComponent(name + ".echo", isDirectory: true)
        let sourcePath = source.volume.resolvingSymlinksInPath().path
        let destinationPath = destination.resolvingSymlinksInPath().path
        guard destinationPath != sourcePath, !destinationPath.hasPrefix(sourcePath + "/") else {
            throw CLIError(message: "Choose a project folder outside \(source.name).")
        }
        guard reader.isConnected(source) else { throw CLIError(message: "Reconnect \(source.name) and try again.") }
        guard !fileManager.fileExists(atPath: target.path) else {
            throw CLIError(
                message: "\(target.lastPathComponent) already exists. Rename the group or choose another folder.")
        }

        let staging = destination.appendingPathComponent(".echo-drone-import-\(UUID().uuidString)", isDirectory: true)
        try fileManager.createDirectory(at: staging, withIntermediateDirectories: false)
        defer { try? fileManager.removeItem(at: staging) }

        let project = staging.appendingPathComponent("project.echo", isDirectory: true)
        let manifest = staging.appendingPathComponent("photos.json")
        try JSONEncoder().encode(grouping.entries(for: photos)).write(to: manifest)
        _ = try await cli.execute(["project", "init", project.path, "--name", name], progress: { _ in })
        let result = try await cli.execute(
            ["--project", project.path, "images", "import", "--manifest", manifest.path], progress: progress)
        let imported = try JSONDecoder().decode(DroneImportResponse.self, from: result).imported
        let originals = Dictionary(uniqueKeysWithValues: imported.map { ($0.hash, $0.id) })
        let receipts = try verify(photos, originals: originals, project: project, target: target, source: source)
        try fileManager.moveItem(at: project, to: target)
        return DroneImportProject(id: group.id, name: name, url: target, receipts: receipts)
    }

    func clean(
        _ files: [DroneMediaFile], on source: DroneSource, verifying receipts: [URL: DroneImportReceipt]
    ) -> DroneCleanupResult {
        var result = DroneCleanupResult()
        for file in files {
            do {
                guard reader.isConnected(source) else {
                    result.failures.append("\(source.name) disconnected. Cleanup stopped.")
                    break
                }
                try reader.validate(file, on: source)
                if let receipt = receipts[file.url] {
                    let originalMatches = try digest(receipt.original) == receipt.hash
                    let sourceMatches = try digest(file.url) == receipt.hash
                    guard originalMatches, sourceMatches else {
                        throw CLIError(message: "The original could not be verified.")
                    }
                    try reader.validate(file, on: source)
                }
                try fileManager.removeItem(at: file.url)
                result.removed.insert(file.url)
            } catch {
                result.failures.append("\(file.url.lastPathComponent) · \(error.localizedDescription)")
            }
        }
        return result
    }

    private func verify(
        _ photos: [DroneImportPhoto], originals: [String: String], project: URL, target: URL, source: DroneSource
    ) throws -> [DroneImportReceipt] {
        let copies = try fileManager.contentsOfDirectory(
            at: project.appendingPathComponent("sources"), includingPropertiesForKeys: nil)
        var names: [String: String] = [:]
        for copy in copies {
            let parts = copy.lastPathComponent.split(separator: ".")
            if parts.count == 3, parts[1] == "original" { names[String(parts[0])] = copy.lastPathComponent }
        }

        var verified: Set<String> = []
        return try photos.map { photo in
            try reader.validate(photo.file, on: source)
            let hash = try digest(photo.id)
            guard let id = originals[hash], let filename = names[id] else {
                throw CLIError(message: "Could not verify the saved copy of \(photo.name).")
            }
            let path = "sources/" + filename
            if verified.insert(hash).inserted, try digest(project.appendingPathComponent(path)) != hash {
                throw CLIError(message: "Could not verify the saved copy of \(photo.name).")
            }
            return DroneImportReceipt(file: photo.file, original: target.appendingPathComponent(path), hash: hash)
        }
    }

    private func digest(_ url: URL) throws -> String {
        let stream = try FileHandle(forReadingFrom: url)
        defer { try? stream.close() }
        var digest = SHA256()
        while let data = try stream.read(upToCount: 1024 * 1024), !data.isEmpty {
            digest.update(data: data)
        }
        return digest.finalize().map { String(format: "%02x", $0) }.joined()
    }
}

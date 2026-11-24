import CoreLocation
import Foundation
import ImageIO

package struct DroneSourceReader: Sendable {
    package init() {}

    private let photoExtensions: Set<String> = ["jpg", "jpeg", "png", "tif", "tiff", "heic", "heif"]
    private let otherMediaExtensions: Set<String> = ["dng", "mp4", "mov", "m4v", "avi", "lrf", "srt"]
    private var fileManager: FileManager { .default }

    package func mountedSources() -> [DroneSource] {
        let keys: [URLResourceKey] = [.volumeIsLocalKey, .volumeIsInternalKey, .volumeIsRemovableKey, .volumeNameKey]
        let volumes = fileManager.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: .skipHiddenVolumes)
        return (volumes ?? []).compactMap { volume in
            guard let values = try? volume.resourceValues(forKeys: Set(keys)),
                values.volumeIsLocal == true,
                values.volumeIsRemovable == true || values.volumeIsInternal == false,
                let identity = volumeIdentity(at: volume)
            else { return nil }

            let folders = cameraFolders(in: volume)
            guard folders.contains(where: containsPhoto) else { return nil }
            return DroneSource(
                volume: volume, cameraFolders: folders, name: values.volumeName ?? volume.lastPathComponent,
                volumeIdentity: identity)
        }.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    package func isConnected(_ source: DroneSource) -> Bool {
        let mounted = fileManager.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: [])
        return mounted?.contains(where: { $0.standardizedFileURL.path == source.volume.standardizedFileURL.path })
            == true
            && volumeIdentity(at: source.volume) == source.volumeIdentity
    }

    package func scan(_ source: DroneSource) throws -> DroneImportScan {
        guard isConnected(source) else { throw DroneSourceError(message: "Reconnect \(source.name) and try again.") }
        let result = try scanFolders(source.cameraFolders.filter { isRealDirectory($0, on: source) })
        guard isConnected(source) else {
            throw DroneSourceError(message: "\(source.name) disconnected during scanning.")
        }
        return result
    }

    package func scanCard(at root: URL) throws -> DroneImportScan {
        let root = root.standardizedFileURL.resolvingSymlinksInPath()
        let folders = cameraFolders(in: root)
        guard !folders.isEmpty else {
            throw DroneSourceError(message: "The source has no DCIM, InternalStorage/DCIM or SD_card/DCIM directory.")
        }

        let result = try scanFolders(folders)
        guard folders.allSatisfy({ fileManager.fileExists(atPath: $0.path) }) else {
            throw DroneSourceError(message: "A camera folder disappeared during scanning.")
        }
        return result
    }

    private func scanFolders(_ folders: [URL]) throws -> DroneImportScan {
        var photos: [DroneImportPhoto] = []
        var media: [DroneMediaFile] = []
        let dateFormatter = DateFormatter()
        dateFormatter.locale = Locale(identifier: "en_US_POSIX")
        dateFormatter.timeZone = TimeZone(secondsFromGMT: 0)
        dateFormatter.dateFormat = "yyyy:MM:dd HH:mm:ss"

        for folder in folders {
            guard let files = files(in: folder) else { continue }
            for case let url as URL in files {
                try Task.checkCancellation()
                let ext = url.pathExtension.lowercased()
                guard photoExtensions.contains(ext) || otherMediaExtensions.contains(ext),
                    let file = try? snapshot(url)
                else { continue }

                media.append(file)
                if photoExtensions.contains(ext) {
                    photos.append(readPhoto(file, dateFormatter: dateFormatter))
                }
            }
        }

        photos.sort {
            let first = $0.capturedAt ?? $0.file.modifiedAt
            let second = $1.capturedAt ?? $1.file.modifiedAt
            return first == second ? $0.id.path < $1.id.path : first < second
        }
        return DroneImportScan(photos: photos, media: media)
    }

    package func validate(_ file: DroneMediaFile, on source: DroneSource) throws {
        let path = file.url.standardizedFileURL.path
        let inCameraFolder = source.cameraFolders.contains {
            path.hasPrefix($0.standardizedFileURL.path + "/")
        }
        guard inCameraFolder,
            file.url.resolvingSymlinksInPath().path == source.volume.resolvingSymlinksInPath().path
                + String(path.dropFirst(source.volume.standardizedFileURL.path.count)),
            volumeIdentity(at: file.url) == source.volumeIdentity,
            try snapshot(file.url) == file
        else {
            throw DroneSourceError(message: "\(file.url.lastPathComponent) changed after scanning.")
        }
    }

    private func snapshot(_ url: URL) throws -> DroneMediaFile {
        let attributes = try fileManager.attributesOfItem(atPath: url.path)
        guard let file = DroneMediaFile(url: url, attributes: attributes) else {
            throw DroneSourceError(message: "Could not read \(url.lastPathComponent).")
        }
        return file
    }

    private func volumeIdentity(at url: URL) -> String? {
        // Resource values may be cached on a URL across mount and unmount events.
        let fresh = URL(fileURLWithPath: url.path)
        guard let values = try? fresh.resourceValues(forKeys: [.volumeUUIDStringKey, .volumeIdentifierKey]) else {
            return nil
        }
        if let uuid = values.volumeUUIDString { return uuid }
        return values.volumeIdentifier.map { String(describing: $0) }
    }

    private func cameraFolders(in volume: URL) -> [URL] {
        let candidates = ["DCIM", "InternalStorage/DCIM", "SD_card/DCIM"].map {
            volume.appendingPathComponent($0, isDirectory: true)
        }
        let resolvedVolume = volume.resolvingSymlinksInPath().path
        return candidates.filter {
            let attributes = try? fileManager.attributesOfItem(atPath: $0.path)
            return attributes?[.type] as? FileAttributeType == .typeDirectory
                && $0.resolvingSymlinksInPath().path
                    == resolvedVolume + String($0.path.dropFirst(volume.path.count))
        }
    }

    private func isRealDirectory(_ folder: URL, on source: DroneSource) -> Bool {
        cameraFolders(in: source.volume).contains(folder) && volumeIdentity(at: folder) == source.volumeIdentity
    }

    private func files(in folder: URL) -> FileManager.DirectoryEnumerator? {
        fileManager.enumerator(
            at: folder, includingPropertiesForKeys: [.isRegularFileKey],
            options: [.skipsHiddenFiles, .skipsPackageDescendants])
    }

    private func containsPhoto(_ folder: URL) -> Bool {
        guard let files = files(in: folder) else { return false }
        for case let url as URL in files {
            if photoExtensions.contains(url.pathExtension.lowercased()),
                (try? url.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true
            {
                return true
            }
        }
        return false
    }

    private func readPhoto(_ file: DroneMediaFile, dateFormatter: DateFormatter) -> DroneImportPhoto {
        let source = CGImageSourceCreateWithURL(file.url as CFURL, nil)
        let properties = source.flatMap { CGImageSourceCopyPropertiesAtIndex($0, 0, nil) } as? [CFString: Any]
        let exif = properties?[kCGImagePropertyExifDictionary] as? [CFString: Any]
        let gps = properties?[kCGImagePropertyGPSDictionary] as? [CFString: Any]
        return DroneImportPhoto(
            file: file,
            capturedAt: (exif?[kCGImagePropertyExifDateTimeOriginal] as? String).flatMap(dateFormatter.date),
            coordinate: coordinate(from: gps))
    }

    private func coordinate(from gps: [CFString: Any]?) -> CLLocationCoordinate2D? {
        guard let latitude = gps?[kCGImagePropertyGPSLatitude] as? Double,
            let longitude = gps?[kCGImagePropertyGPSLongitude] as? Double
        else { return nil }

        let south = (gps?[kCGImagePropertyGPSLatitudeRef] as? String)?.uppercased() == "S"
        let west = (gps?[kCGImagePropertyGPSLongitudeRef] as? String)?.uppercased() == "W"
        let position = CLLocationCoordinate2D(
            latitude: south ? -abs(latitude) : latitude, longitude: west ? -abs(longitude) : longitude)
        return CLLocationCoordinate2DIsValid(position) ? position : nil
    }
}

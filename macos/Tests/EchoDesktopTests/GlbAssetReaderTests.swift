import AppKit
import XCTest

@testable import EchoDesktop

final class GlbAssetReaderTests: XCTestCase {
    func testBaseColourFollowsPrimitiveMaterialAndTextureSource() throws {
        let file = try fixture(textureSource: 1)
        defer { try? FileManager.default.removeItem(at: file) }
        let asset = try GlbAssetReader().read(file)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(asset.image.tiffRepresentation)))
        let colour = try XCTUnwrap(bitmap.colorAt(x: 0, y: 0)?.usingColorSpace(.deviceRGB))
        XCTAssertGreaterThan(colour.redComponent, 0.9)
        XCTAssertGreaterThan(colour.greenComponent, 0.9)
        XCTAssertLessThan(colour.blueComponent, 0.1)
        XCTAssertEqual(asset.surface.faces.count, 1)
    }

    func testInvalidBaseColourImageDoesNotFallBackToNormalMap() throws {
        let file = try fixture(textureSource: 99)
        defer { try? FileManager.default.removeItem(at: file) }
        XCTAssertThrowsError(try GlbAssetReader().read(file))
    }

    private func fixture(textureSource: Int) throws -> URL {
        var binary = Data()
        for value: Float in [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1] {
            append(value.bitPattern, to: &binary)
        }
        for index: UInt32 in [0, 1, 2] { append(index, to: &binary) }
        let normal = try pixel(red: 0.5, green: 0.5, blue: 1)
        let colour = try pixel(red: 1, green: 1, blue: 0)
        let normalOffset = binary.count
        binary.append(normal)
        while !binary.count.isMultiple(of: 4) { binary.append(0) }
        let colourOffset = binary.count
        binary.append(colour)
        while !binary.count.isMultiple(of: 4) { binary.append(0) }
        let root: [String: Any] = [
            "asset": ["version": "2.0"],
            "buffers": [["byteLength": binary.count]],
            "bufferViews": [
                ["buffer": 0, "byteOffset": 0, "byteLength": 36],
                ["buffer": 0, "byteOffset": 36, "byteLength": 24],
                ["buffer": 0, "byteOffset": 60, "byteLength": 12],
                ["buffer": 0, "byteOffset": normalOffset, "byteLength": normal.count],
                ["buffer": 0, "byteOffset": colourOffset, "byteLength": colour.count],
            ],
            "accessors": [
                ["bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"],
                ["bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC2"],
                ["bufferView": 2, "componentType": 5125, "count": 3, "type": "SCALAR"],
            ],
            "meshes": [
                ["primitives": [["attributes": ["POSITION": 0, "TEXCOORD_0": 1], "indices": 2, "material": 1]]]
            ],
            "materials": [
                ["pbrMetallicRoughness": ["baseColorTexture": ["index": 0]]],
                ["normalTexture": ["index": 0], "pbrMetallicRoughness": ["baseColorTexture": ["index": 2]]],
            ],
            "textures": [["source": 0], ["source": 0], ["source": textureSource]],
            "images": [["bufferView": 3, "mimeType": "image/png"], ["bufferView": 4, "mimeType": "image/png"]],
        ]
        var json = try JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])
        while !json.count.isMultiple(of: 4) { json.append(32) }
        var glb = Data()
        for value in [UInt32(0x4654_6c67), 2, UInt32(28 + json.count + binary.count), UInt32(json.count), 0x4e4f_534a] {
            append(value, to: &glb)
        }
        glb.append(json)
        append(UInt32(binary.count), to: &glb)
        append(0x004e_4942, to: &glb)
        glb.append(binary)
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("echo-glb-colour-\(UUID()).glb")
        try glb.write(to: file)
        return file
    }

    private func append(_ value: UInt32, to data: inout Data) {
        var littleEndian = value.littleEndian
        withUnsafeBytes(of: &littleEndian) { data.append(contentsOf: $0) }
    }

    private func pixel(red: CGFloat, green: CGFloat, blue: CGFloat) throws -> Data {
        let bitmap = try XCTUnwrap(
            NSBitmapImageRep(
                bitmapDataPlanes: nil, pixelsWide: 1, pixelsHigh: 1, bitsPerSample: 8,
                samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                colorSpaceName: .deviceRGB, bytesPerRow: 4, bitsPerPixel: 32))
        bitmap.setColor(NSColor(deviceRed: red, green: green, blue: blue, alpha: 1), atX: 0, y: 0)
        return try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
    }
}

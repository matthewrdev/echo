import Foundation

struct SkyProbabilityMap: Decodable {
    let imageId: String
    let width: Int
    let height: Int
    let sky: [UInt8]

    func matches(_ map: DepthMap) -> Bool {
        let size = width.multipliedReportingOverflow(by: height)
        return imageId == map.imageId && width > 0 && height > 0 && !size.overflow
            && size.partialValue == sky.count
    }

    func confidentCells() -> [Bool] {
        var cells = [Bool](repeating: false, count: sky.count)
        for y in 0..<height {
            let nextY = min(y + 1, height - 1)
            for x in 0..<width {
                let nextX = min(x + 1, width - 1)
                // Match DepthSkyFilter: all four labels must be at least 80%
                // sky. Uncertain skyline pixels remain in the coverage score.
                let minimum = min(
                    min(sky[y * width + x], sky[y * width + nextX]),
                    min(sky[nextY * width + x], sky[nextY * width + nextX]))
                cells[y * width + x] = minimum >= 204
            }
        }
        return cells
    }
}

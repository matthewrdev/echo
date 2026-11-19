import Foundation

// Exact 24-bit RGB frequencies take 64 MiB regardless of cloud size. No point array is retained.
struct ColourFrequencies: Sendable {
    let bins: [UInt32]
    let scanned: Int
    let included: Int

    func histogram(grade: ColourGrade) throws -> ColourHistogram {
        guard grade.isValid else {
            throw CLIError(message: "This colour grade is invalid or from an unsupported version.")
        }
        let transform = ColourGradeTransform(grade)
        var result = ColourHistogram()
        for index in bins.indices {
            if index.isMultiple(of: 65536) { try Task.checkCancellation() }
            guard bins[index] > 0 else { continue }
            let colour = SIMD3(UInt8((index >> 16) & 255), UInt8((index >> 8) & 255), UInt8(index & 255))
            result.add(transform.apply(colour), count: UInt64(bins[index]))
        }
        return result
    }
}

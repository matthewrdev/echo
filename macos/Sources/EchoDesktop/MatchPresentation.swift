import AppKit

struct MatchPresentation {
    let first: Photo
    let second: Photo
    let firstImage: NSImage
    let secondImage: NSImage
    let matches: [ImageMatch]
    var diagnostics: MatchingDiagnostics? = nil
}

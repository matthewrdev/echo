import Foundation

package struct Surface: Decodable {
    package let vertices: [[Double]]
    package let faces: [[Int]]
    package let sourceImages: [String]

    package init(vertices: [[Double]], faces: [[Int]], sourceImages: [String]) {
        self.vertices = vertices
        self.faces = faces
        self.sourceImages = sourceImages
    }
}

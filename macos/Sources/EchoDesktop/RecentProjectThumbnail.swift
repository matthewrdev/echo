import SwiftUI

struct RecentProjectThumbnail: View {
    let project: RecentProject
    let loader: ProjectThumbnailLoader?
    @State private var image: NSImage?

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 7).fill(.quaternary)
            if let image {
                Image(nsImage: image).resizable().scaledToFit()
            } else {
                Image(systemName: "folder.fill").font(.title2).foregroundStyle(Color.accentColor)
            }
        }
        .frame(width: 88, height: 66).clipShape(RoundedRectangle(cornerRadius: 7))
        .accessibilityLabel(image == nil ? "Project folder" : "Saved mesh preview")
        .task(id: project.lastOpened) {
            let loaded = await loader?.load(root: URL(fileURLWithPath: project.path))
            guard !Task.isCancelled else { return }
            image = loaded
        }
    }
}

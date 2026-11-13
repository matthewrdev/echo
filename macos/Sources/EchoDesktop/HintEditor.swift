import SwiftUI

struct HintEditor: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var latitude = ""
    @State private var longitude = ""
    @State private var altitude = ""
    @State private var yaw = ""
    @State private var pitch = ""
    @State private var roll = ""
    @State private var otherImage = ""
    @State private var ax = ""
    @State private var ay = ""
    @State private var bx = ""
    @State private var by = ""

    var body: some View {
        Form {
            if let photo = workspace.selectedPhoto {
                Section("Camera hint · \(photo.name)") {
                    Text(
                        "GPS and orientation are starting estimates. Leave both coordinates or both orientation angles blank to supply only the other measurement."
                    ).foregroundStyle(.secondary)
                    HStack {
                        TextField("Latitude", text: $latitude)
                        TextField("Longitude", text: $longitude)
                        TextField("Altitude (m)", text: $altitude)
                    }
                    HStack {
                        TextField("Yaw (°)", text: $yaw)
                        TextField("Pitch (°)", text: $pitch)
                        TextField("Roll (°)", text: $roll)
                    }
                    Button("Save camera hint") { saveCamera(photo) }.disabled(!workspace.canEdit)
                }
                Section("Image overlap and tie points") {
                    Picker("Other photograph", selection: $otherImage) {
                        Text("Select another image").tag("")
                        ForEach((workspace.inspection?.project.images ?? []).filter { $0.id != photo.id }) { image in
                            Text(image.name).tag(image.id)
                        }
                    }
                    Button("Suggest these images overlap") {
                        workspace.save(hint: ImageHint(type: "overlap", imageId: photo.id, otherImageId: otherImage))
                    }.disabled(!workspace.canEdit || otherImage.isEmpty)
                    Text(
                        "Tie points use full-resolution pixels in the normalized photos. Identify the same physical point in both images."
                    ).foregroundStyle(.secondary)
                    HStack {
                        TextField("This image X", text: $ax)
                        TextField("Y", text: $ay)
                        TextField("Other image X", text: $bx)
                        TextField("Y", text: $by)
                    }
                    Button("Save tie point") {
                        guard let x1 = Double(ax), let y1 = Double(ay), let x2 = Double(bx), let y2 = Double(by) else {
                            workspace.errorMessage = "Enter four numeric pixel coordinates."
                            return
                        }
                        workspace.save(
                            hint: ImageHint(
                                type: "tie", imageId: photo.id, otherImageId: otherImage, pixel: [x1, y1],
                                otherPixel: [x2, y2]))
                    }.disabled(!workspace.canEdit || otherImage.isEmpty)
                }
                Section("Saved hints") {
                    ForEach((workspace.inspection?.project.hints ?? []).filter { $0.imageId == photo.id }) { hint in
                        HStack {
                            Text("\(hint.type.capitalized) · \(hint.enabled ? "enabled" : "disabled")")
                            Spacer()
                            Button("Disable") { workspace.disable(hint: hint) }.disabled(
                                !workspace.canEdit || !hint.enabled)
                        }
                    }
                }
            } else {
                Text("Select a photograph to add placement, overlap, or tie-point hints.")
            }
        }
        .formStyle(.grouped)
        .task(id: workspace.selectedPhotoID) {
            let photo = workspace.selectedPhoto
            latitude = photo?.gps.map { String($0.latitude) } ?? ""
            longitude = photo?.gps.map { String($0.longitude) } ?? ""
            altitude = photo?.gps?.altitude.map { String($0) } ?? ""
            yaw = photo?.attitude.map { String($0.yaw) } ?? ""
            pitch = photo?.attitude.map { String($0.pitch) } ?? ""
            roll = photo?.attitude.map { String($0.roll) } ?? ""
        }
    }

    private func saveCamera(_ photo: Photo) {
        var hint = ImageHint(type: "camera", imageId: photo.id)
        if !latitude.isEmpty || !longitude.isEmpty {
            guard let lat = Double(latitude), let lon = Double(longitude) else {
                workspace.errorMessage = "Enter both latitude and longitude as numbers."
                return
            }
            hint.position = GeoPosition(
                latitude: lat, longitude: lon, altitude: Double(altitude), altitudeDatum: "unknown",
                uncertaintyMetres: 10)
        }
        if !yaw.isEmpty || !pitch.isEmpty {
            guard let yawValue = Double(yaw), let pitchValue = Double(pitch) else {
                workspace.errorMessage = "Enter both yaw and pitch as numbers."
                return
            }
            hint.attitude = Attitude(yaw: yawValue, pitch: pitchValue, roll: Double(roll) ?? 0)
        }
        workspace.save(hint: hint)
    }
}

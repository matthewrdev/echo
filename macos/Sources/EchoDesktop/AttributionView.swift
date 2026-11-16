import SwiftUI

struct AttributionView: View {
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Label("Dependencies & Attribution", systemImage: "doc.text.magnifyingglass")
                    .font(.title2.bold())
                Spacer()
            }.padding(20)
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    Group {
                        Text("Optional sky, rock and vegetation detection").font(.headline)
                        Text(
                            "Exclude sky from depth, Recover missing cliff detail and Optimise vegetation use a pretrained image-segmentation model. All three options are off for new projects. Existing projects keep their saved settings."
                        )
                        Text("Why Hugging Face?").font(.headline)
                        Text(
                            "Hugging Face hosts the model file selected for Echo. It is a download dependency: the app fetches about 228 MB on first enabled use. ONNX Runtime then runs the model locally. Echo does not send photographs to Hugging Face or use its paid inference service. A network connection is needed if the model is not cached."
                        )
                        Text("What gets cached?").font(.headline)
                        Text(
                            "The verified model is shared by projects in ~/Library/Caches/Echo/models. Existing project-local models are copied into this cache when needed. Cached models work offline; clearing the cache can require another download. Each project separately saves classifications for unchanged photographs. Changing the model, classification algorithm or explicitly resetting Depth can require fresh classification."
                        )
                    }
                    Group {
                        Text("Model attribution").font(.headline)
                        Text(
                            "UPerNet with a ConvNeXt-Tiny backbone, published by OpenMMLab and trained on ADE20K. The downloaded ONNX conversion is published by JElvis using the original fp32 weights. Both repositories declare MIT licensing."
                        )
                        Link(
                            "OpenMMLab source model",
                            destination: URL(string: "https://huggingface.co/openmmlab/upernet-convnext-tiny")!)
                        Link(
                            "JElvis conversion and license",
                            destination: URL(string: "https://huggingface.co/JElvis/upernet-convnext-tiny-ade-onnx")!)
                        DisclosureGroup("Pinned model version") {
                            Text("Revision: a8e2807c3479bc617b37d8ed53e68660b3fa1ebb")
                            Text("SHA-256: fea729ab6c5d338bc79ad3d986b073e6faa12c97255eafd78a1f7145326e534e")
                        }.font(.caption)
                        Text("Commercial-use status").font(.headline)
                        Text(
                            "The declared MIT licenses permit commercial use and redistribution with the required notices. However, ADE20K's original-image terms restrict dataset use to non-commercial research and education. Those terms do not explicitly settle commercial use of pretrained weights. Echo does not distribute the dataset. The weights need licensing review before commercial release; their MIT declaration is not complete commercial clearance."
                        )
                        Link("ADE20K dataset terms", destination: URL(string: "https://ade20k.csail.mit.edu/terms/")!)
                        Link(
                            "Hugging Face service terms",
                            destination: URL(string: "https://huggingface.co/terms-of-service")!)
                    }
                    Group {
                        Text("Local inference dependencies").font(.headline)
                        Text(
                            "Microsoft ONNX Runtime 1.23.2 (MIT), its managed C# bindings 1.23.2 (MIT), and System.Numerics.Tensors 9.0.0 (MIT). ONNX Runtime includes native libraries and additional third-party notices. Python, PyTorch, Transformers and the Hugging Face client are not installed by this feature."
                        )
                        Link(
                            "ONNX Runtime license",
                            destination: URL(string: "https://github.com/microsoft/onnxruntime/blob/v1.23.2/LICENSE")!)
                        Text("Other Echo dependencies").font(.headline)
                        Text(
                            "Echo also uses MetadataExtractor, XmpCore, SkiaSharp, MathNet.Numerics, System.ComponentModel.Composition and the bundled PoissonRecon worker. The packaged notices describe these dependencies and include the bundled license texts."
                        )
                        if let resources = Bundle.main.resourceURL,
                            FileManager.default.fileExists(
                                atPath: resources.appendingPathComponent("THIRD_PARTY_NOTICES.md").path)
                        {
                            Button("Open all third-party notices") {
                                NSWorkspace.shared.open(resources.appendingPathComponent("THIRD_PARTY_NOTICES.md"))
                            }
                            Button("Open bundled license texts") {
                                NSWorkspace.shared.open(resources.appendingPathComponent("licenses", isDirectory: true))
                            }
                        }
                        Text("Licensing sources reviewed 16 November 2026.").font(.caption).foregroundStyle(.secondary)
                    }
                }
                .font(.callout).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).padding(20)
            }
            Divider()
            HStack {
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }.padding(16)
        }.frame(width: 720, height: 720)
    }
}

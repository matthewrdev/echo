# Metadata dependencies

Echo reads metadata inside its C# process using [MetadataExtractor 2.9.3](https://github.com/drewnoakes/metadata-extractor-dotnet) and its [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet) dependency. MetadataExtractor uses Apache License 2.0; XmpCore uses the BSD license. Copyright notices and the license are included in `licenses/` and in the packaged app's Resources directory.

DJI debug payloads are retained as original bytes. Their private histogram/debug layouts are not assumed to be documented numeric arrays.

Other .NET dependencies retain their upstream licenses; see the project files and NuGet package metadata for versions and terms.

# Local scene classification

Optional vegetation budgets and cliff recovery use [Microsoft ONNX Runtime 1.23.2](https://github.com/microsoft/onnxruntime/tree/v1.23.2), under the MIT license (`licenses/ONNXRuntime-MIT.txt`).

The classifier is [OpenMMLab UperNet with ConvNeXt-Tiny](https://huggingface.co/openmmlab/upernet-convnext-tiny), trained on ADE20K. Echo downloads the unmodified fp32 [ONNX conversion by JElvis](https://huggingface.co/JElvis/upernet-convnext-tiny-ade-onnx/tree/a8e2807c3479bc617b37d8ed53e68660b3fa1ebb), revision `a8e2807c3479bc617b37d8ed53e68660b3fa1ebb`, SHA-256 `fea729ab6c5d338bc79ad3d986b073e6faa12c97255eafd78a1f7145326e534e`. The MIT notice is included in `licenses/UperNet-ConvNeXt-MIT.txt`. Weights are downloaded on first enabled use rather than redistributed in the app bundle. Input uses 512×512 RGB with ImageNet normalization; ADE20K class probabilities are grouped into vegetation and rock without using colour thresholds.

# Screened Poisson surface reconstruction

Echo's C# surface-completion subsystem runs a bundled, isolated native worker built from [PoissonRecon](https://github.com/mkazhdan/PoissonRecon), version 18.76, commit `262b0f539d404057d1f36e1adc07fc9388678899`. The repository MIT license is included in `licenses/PoissonRecon-MIT.txt`; the source copyright and redistribution notices are reproduced in `licenses/PoissonRecon-source-notices.txt` and retained in the vendored headers. Both are included in the packaged app's Resources directory. No external installation or runtime download is required. The worker uses double precision and serial execution, with upstream `SANITIZED_PR` synchronization enabled.

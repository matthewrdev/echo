# Metadata dependencies

Echo reads metadata inside its C# process using [MetadataExtractor 2.9.3](https://github.com/drewnoakes/metadata-extractor-dotnet) and its [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet) dependency. MetadataExtractor uses Apache License 2.0; XmpCore uses the BSD license. Copyright notices and the license are included in `licenses/` and in the packaged app's Resources directory.

DJI debug payloads are retained as original bytes. Their private histogram/debug layouts are not assumed to be documented numeric arrays.

Other .NET dependencies retain their upstream licenses; see the project files and NuGet package metadata for versions and terms.

# Local scene classification

Sky exclusion, vegetation budgets and cliff recovery use a pretrained image-segmentation model, executed locally by ONNX Runtime. Hugging Face hosts the model download; Echo does not use its hosted inference API or upload photographs for classification.

The added software dependencies are:

| Dependency | Version | Purpose | License and notices |
| --- | --- | --- | --- |
| Microsoft.ML.OnnxRuntime | 1.23.2 | Native inference engine, including `libonnxruntime.dylib` on macOS | MIT; `licenses/ONNXRuntime-MIT.txt` and `licenses/ONNXRuntime-ThirdPartyNotices.txt` |
| Microsoft.ML.OnnxRuntime.Managed | 1.23.2 | C# bindings, pulled in by ONNX Runtime | Same upstream MIT license and third-party notices |
| System.Numerics.Tensors | 9.0.0 | Transitive dependency of the managed bindings | MIT; `licenses/System.Numerics.Tensors-MIT.txt` and `licenses/System.Numerics.Tensors-ThirdPartyNotices.txt` |

The third-party notice files are copied from these exact NuGet package versions. They cover upstream components across supported builds; inclusion in a notice file does not mean every listed component is loaded by Echo's macOS build. The standalone package includes the entire `licenses/` directory.

The classifier is [OpenMMLab UperNet with ConvNeXt-Tiny](https://huggingface.co/openmmlab/upernet-convnext-tiny), trained on ADE20K. Echo downloads the unmodified fp32 [ONNX conversion by JElvis](https://huggingface.co/JElvis/upernet-convnext-tiny-ade-onnx/tree/a8e2807c3479bc617b37d8ed53e68660b3fa1ebb), revision `a8e2807c3479bc617b37d8ed53e68660b3fa1ebb`, SHA-256 `fea729ab6c5d338bc79ad3d986b073e6faa12c97255eafd78a1f7145326e534e`. Both model repositories declare MIT licensing; the conversion's notice is included in `licenses/UperNet-ConvNeXt-MIT.txt`. Weights are downloaded on first enabled use rather than redistributed in the app bundle. Input uses 512×512 RGB with ImageNet normalization; ADE20K class probabilities are grouped into sky, vegetation and rock without using colour thresholds. Echo does not install Python, PyTorch, Transformers, the Hugging Face client or the ADE20K dataset for this feature.

**Commercial status of the weights remains unresolved.** MIT permits commercial use and redistribution subject to retaining the required notices, but this declaration alone does not resolve training-data provenance. The [ADE20K original-image terms](https://ade20k.csail.mit.edu/terms/) restrict use of the image database to non-commercial research and education; the site's software and annotations have separate BSD terms. The published image terms do not explicitly settle downstream commercial use of pretrained weights. Echo does not distribute the dataset. Commercial distribution of these weights or a product relying on them needs licensing review or clarification from the relevant rights holders; this notice is not commercial clearance. Sources checked on 16 November 2026.

Hugging Face access is governed separately by its [service terms](https://huggingface.co/terms-of-service). The implementation makes an unauthenticated HTTPS file download and contains no paid inference integration or API key. Initial installation depends on the third-party repository and its download infrastructure being available. Pinning the revision and checking SHA-256 verifies the expected file; it does not establish licensing rights or availability guarantees.

# Screened Poisson surface reconstruction

Echo's C# surface-completion subsystem runs a bundled, isolated native worker built from [PoissonRecon](https://github.com/mkazhdan/PoissonRecon), version 18.76, commit `262b0f539d404057d1f36e1adc07fc9388678899`. The repository MIT license is included in `licenses/PoissonRecon-MIT.txt`; the source copyright and redistribution notices are reproduced in `licenses/PoissonRecon-source-notices.txt` and retained in the vendored headers. Both are included in the packaged app's Resources directory. No external installation or runtime download is required. The worker uses double precision and serial execution, with upstream `SANITIZED_PR` synchronization enabled.

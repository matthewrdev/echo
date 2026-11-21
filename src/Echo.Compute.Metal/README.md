# Echo.Compute.Metal

Runs shared compute operations on Metal, including matching, fusion, surface analysis and texturing. Swift owns the device and buffers. C# chooses kernels and owns the CPU paths.

| Shader | Kernel inputs and outputs |
| --- | --- |
| `00Common.metal` and `02DepthViews.metal` | Shared math and camera/depth sampling for kernels that project between views. |
| `01Descriptors.metal` | Computes descriptor distances and reduces them to nearest-neighbour candidates, optionally in tiles. |
| `03Gaussian.metal` | Blurs feature images in separate horizontal and vertical passes. |
| `03DepthFilter.metal` | Filters estimated depth and confidence against neighbouring samples. |
| `03Cloud.metal` | Tests depth agreement across views and reports support for cloud points. |
| `03Fusion.metal` and `03FusionCompaction.metal` | Integrate depth evidence into volume samples and compact retained results into the output buffer. |
| `04Surface.metal` | Indexes volume samples, locates active surface cells and evaluates visibility, occlusion and semantic evidence. |
| `05Seams.metal` | Diffuses seam corrections between linked texels. |
| `06Texture.metal` | Projects photo pixels into atlas charts and fills texture padding. |
| `07Epipolar.metal` | Finds and counts descriptor pairs that pass an epipolar filter. |
| `07Triangulation.metal` | Triangulates matched image observations into candidate 3D points. |
| `08UvSolve.metal` | Computes the iterative warm start for UV coordinates from the chart’s sparse linear system. |

Swift binds buffers in the order declared by each kernel’s `[[buffer(n)]]` arguments. The leading documentation headers are excluded before compiling or fingerprinting the kernels. Changes to the shader body affect artifact reuse.

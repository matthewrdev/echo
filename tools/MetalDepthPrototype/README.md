# Experimental Metal depth-cost benchmark

This prototype evaluates the existing 25-sample stereo patch cost on a Metal GPU. It is a benchmark only; Echo's processing pipeline continues to use the CPU. The shader uses float32 with safe math, precise math functions, explicit bilinear sampling and multiply/add contraction disabled. Those settings do not make it equivalent to the CPU's double-precision arithmetic.

From the repository root:

```sh
dotnet build tools/Echo.Benchmarks -c Release
xcrun swiftc -O tools/MetalDepthPrototype/*.swift -o /tmp/echo-metal-depth
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll metal-depth export PROJECT RUN 640 /tmp/echo-metal-fixture 4
/tmp/echo-metal-depth /tmp/echo-metal-fixture tools/MetalDepthPrototype/StereoCost.metal 7
dotnet tools/Echo.Benchmarks/bin/Release/net10.0/Echo.Benchmarks.dll metal-depth verify PROJECT RUN 640 /tmp/echo-metal-fixture 4
```

The final export/verify argument is the pixel sampling stride. An optional camera index follows it; the default is zero. Use identical arguments for export and verify, and keep the saved run and source images unchanged. GPU execution requires access to the machine's Metal device.

The exporter uses the saved alignment, actual neighbour selection, grayscale resizing, patch normalization and automatic depth range. It evaluates full-range search hypotheses on a regular grid of textured pixels. Each GPU thread evaluates one pixel/depth hypothesis. The CPU comparison uses the existing SIMD cost calculation with the normal total CPU worker budget. The validator also checks its decision logic against the production search before exporting the fixture.

`fixture.json` records four CPU timings. `gpu-metrics.json` records GPU dispatch wall times, GPU execution times, input-buffer loading/upload and pipeline setup separately. Compare medians after discarding the first CPU/GPU iteration. These are cost-kernel measurements, not full depth-pass timings: refinement scheduling, spatial filtering and production interop are not implemented on the GPU. The upload figure includes reading the exported buffers from disk. Binary fixtures are local little-endian float32/float64 data, not project artifacts.

`validation.json` compares finite support, rounded costs, best hypotheses, acceptance decisions, interpolated depths and confidence values. Ambiguity checking and interpolation reuse the production CPU methods, isolating changes introduced by GPU costs. Any acceptance or output differences mean this prototype has failed numerical-equivalence validation; a small error rate does not authorize enabling it by default. A hybrid implementation would need a conservative error bound and CPU fallback that can recover missed candidates, not merely re-score the GPU's winning candidate.

The initial M5 Pro experiment used 40,631 searches, 192 hypotheses and 12 neighbours across four batches at 160, 640 and 2048 pixels. Warm cost-kernel dispatch was approximately 7.0–7.35 times faster than the 16-worker SIMD CPU calculation. It changed three acceptance decisions and some depths/confidences, so it is not a production replacement.

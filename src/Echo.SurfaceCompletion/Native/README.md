# Screened Poisson worker

`PoissonRecon/` is an unmodified source snapshot of Michael Kazhdan's
[PoissonRecon](https://github.com/mkazhdan/PoissonRecon), version 18.76,
commit `262b0f539d404057d1f36e1adc07fc9388678899`.
The repository MIT license and source redistribution notices are included in
the root `licenses/` directory and packaged with Echo.

The C# subsystem owns input preparation, process lifetime, cancellation and
output validation. The small executable calls the upstream reconstruction API
with double precision, point weight 4 and a resolution derived from the fusion
voxel size. A Dirichlet boundary condition and a 1.25 domain scale close missing
caps within the padded solve domain. Upstream global threading state stays inside this isolated process.
Serial execution and `SANITIZED_PR` avoid the parallel extractor's ARM64 flag
races. No external process installation or runtime download is needed.

Input is little endian: magic `0x49504345`, version `1` (both uint32), sample
count (uint64), then six doubles per sample (position and oriented unit normal).
Output is magic `0x4f504345`, version `1`, vertex count and triangle count
(uint64), three doubles per vertex, then three uint32 indices per triangle.

The macOS build compiles both arm64 and x86_64 and copies the universal worker
alongside the .NET processor. It links only macOS system libraries.

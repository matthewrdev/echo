using Echo.Meshing;
using Echo.Models;
using Echo.Projects;

namespace Echo.ImageProjection;

internal sealed record ExternalRegistrationRequest(
    IProjectStore Store, RunData Run, ReconstructionOverview Scene,
    Photo Photo, string ImagePath, IReadOnlyList<ImageGeometryControl>? Controls,
    IReadOnlyList<string> ReferenceImageIds, double? FocalPixels, IMeshRaycaster Mesh);

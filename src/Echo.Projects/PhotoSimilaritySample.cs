using Echo.Contracts;
using Echo.Imaging;

namespace Echo.Projects;

public sealed record PhotoSimilaritySample(Photo Photo, GrayscaleImage Image, GrayscaleImage Overview, double Sharpness, bool Protected);

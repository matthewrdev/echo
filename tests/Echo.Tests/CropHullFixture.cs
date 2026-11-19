using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Tests;

public static class CropHullFixture
{
    public static CropHull Box(V3 centre, V3 size)
    {
        var min = centre - size * .5;
        var max = centre + size * .5;
        return new CropHull
        {
            Vertices =
            [
                [min.X, min.Y, min.Z], [max.X, min.Y, min.Z], [max.X, max.Y, min.Z], [min.X, max.Y, min.Z],
                [min.X, min.Y, max.Z], [max.X, min.Y, max.Z], [max.X, max.Y, max.Z], [min.X, max.Y, max.Z]
            ],
            Planes =
            [
                new CropPlane { NormalX = 1, Offset = max.X },
                new CropPlane { NormalX = -1, Offset = -min.X },
                new CropPlane { NormalY = 1, Offset = max.Y },
                new CropPlane { NormalY = -1, Offset = -min.Y },
                new CropPlane { NormalZ = 1, Offset = max.Z },
                new CropPlane { NormalZ = -1, Offset = -min.Z }
            ]
        };
    }
}

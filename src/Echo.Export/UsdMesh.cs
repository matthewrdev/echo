using Echo.Models;

namespace Echo.Export;

internal static class UsdMesh
{
    public static void Write(TextWriter writer, Surface mesh, IList<double[]> uvs)
    {
        writer.WriteLine("""
            #usda 1.0
            (
                defaultPrim = "Cliff"
                metersPerUnit = 1
                upAxis = "Y"
            )

            def Xform "Cliff"
            {
                def Mesh "Mesh" (
                    prepend apiSchemas = ["MaterialBindingAPI"]
                )
                {
                    rel material:binding = </Cliff/Material>
                    uniform token subdivisionScheme = "none"
                    uniform bool doubleSided = true
                    point3f[] points = [
            """);
        foreach (var vertex in mesh.Vertices)
        {
            writer.WriteLine(FormattableString.Invariant($"({(float)vertex[0]:R}, {(float)vertex[1]:R}, {(float)vertex[2]:R}),"));
        }

        writer.WriteLine("] \n int[] faceVertexCounts = [");
        for (var index = 0; index < mesh.Faces.Count; index++)
        {
            writer.WriteLine("3,");
        }

        writer.WriteLine("] \n int[] faceVertexIndices = [");
        foreach (var triangle in mesh.Faces)
        {
            writer.WriteLine(FormattableString.Invariant($"{triangle[0]}, {triangle[1]}, {triangle[2]},"));
        }

        writer.WriteLine("] \n texCoord2f[] primvars:st = [");
        foreach (var uv in uvs)
        {
            writer.WriteLine(FormattableString.Invariant($"({(float)uv[0]:R}, {(float)(1 - uv[1]):R}),"));
        }

        writer.WriteLine("""
                    ] (
                        interpolation = "vertex"
                    )
                }

                def Material "Material"
                {
                    token outputs:surface.connect = </Cliff/Material/Surface.outputs:surface>

                    def Shader "Surface"
                    {
                        uniform token info:id = "UsdPreviewSurface"
                        color3f inputs:diffuseColor.connect = </Cliff/Material/Atlas.outputs:rgb>
                        float inputs:roughness = 1
                        float inputs:metallic = 0
                        token outputs:surface
                    }

                    def Shader "Atlas"
                    {
                        uniform token info:id = "UsdUVTexture"
                        asset inputs:file = @atlas.jpg@
                        token inputs:sourceColorSpace = "sRGB"
                        token inputs:wrapS = "clamp"
                        token inputs:wrapT = "clamp"
                        float2 inputs:st.connect = </Cliff/Material/UV.outputs:result>
                        float3 outputs:rgb
                    }

                    def Shader "UV"
                    {
                        uniform token info:id = "UsdPrimvarReader_float2"
                        string inputs:varname = "st"
                        float2 outputs:result
                    }
                }
            }
            """);
    }
}

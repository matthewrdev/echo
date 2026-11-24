using Echo.Models;

namespace Echo.Export;

internal static class FbxMesh
{
    public static void Write(string path, Surface surface, IList<double[]> uvs)
    {
        const long model = 1001;
        const long geometry = 1002;
        const long material = 1003;
        const long texture = 1004;
        const long video = 1005;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var fbx = new FbxNodeWriter(writer);
        fbx.Begin();
        fbx.Node("FBXHeaderExtension", () =>
        {
            fbx.Value("FBXHeaderVersion", 1003);
            fbx.Value("FBXVersion", 7400);
            fbx.Value("EncryptionType", 0);
            fbx.Value("Creator", "Echo");
        });
        fbx.Value("Creator", "Echo");
        fbx.Node("GlobalSettings", () =>
        {
            fbx.Value("Version", 1000);
            fbx.Node("Properties70", () =>
            {
                fbx.Value("P", "UpAxis", "int", "Integer", "", 1);
                fbx.Value("P", "UpAxisSign", "int", "Integer", "", 1);
                fbx.Value("P", "FrontAxis", "int", "Integer", "", 2);
                fbx.Value("P", "FrontAxisSign", "int", "Integer", "", 1);
                fbx.Value("P", "CoordAxis", "int", "Integer", "", 0);
                fbx.Value("P", "CoordAxisSign", "int", "Integer", "", 1);
                fbx.Value("P", "UnitScaleFactor", "double", "Number", "", 100d);
                fbx.Value("P", "OriginalUnitScaleFactor", "double", "Number", "", 100d);
            });
        });
        fbx.Node("Documents", () =>
        {
            fbx.Value("Count", 1);
            fbx.Node("Document", () => fbx.Value("RootNode", 0L), 1000L, "Scene", "Scene");
        });
        fbx.Value("References");
        WriteDefinitions(fbx);
        fbx.Node("Objects", () =>
        {
            fbx.Node("Geometry", () => WriteGeometry(fbx, surface, uvs), geometry, "Cliff\0\u0001Geometry", "Mesh");
            fbx.Node("Model", () =>
            {
                fbx.Value("Version", 232);
                fbx.Value("Shading", true);
                fbx.Value("Culling", "CullingOff");
            }, model, "Cliff\0\u0001Model", "Mesh");
            WriteMaterial(fbx, material, texture, video);
        });
        fbx.Node("Connections", () =>
        {
            fbx.Value("C", "OO", model, 0L);
            fbx.Value("C", "OO", geometry, model);
            fbx.Value("C", "OO", material, model);
            fbx.Value("C", "OP", texture, material, "DiffuseColor");
            fbx.Value("C", "OO", video, texture);
        });
        fbx.Node("Takes", () => fbx.Value("Current", ""));
        fbx.Finish();
    }

    private static void WriteDefinitions(FbxNodeWriter fbx)
    {
        fbx.Node("Definitions", () =>
        {
            fbx.Value("Version", 100);
            fbx.Value("Count", 6);
            foreach (var type in new[] { "GlobalSettings", "Geometry", "Model", "Material", "Texture", "Video" })
            {
                fbx.Node("ObjectType", () => fbx.Value("Count", 1), type);
            }
        });
    }

    private static void WriteGeometry(FbxNodeWriter fbx, Surface surface, IList<double[]> uvs)
    {
        fbx.Value("GeometryVersion", 124);
        fbx.Value("Vertices", new FbxArray((byte)'d', checked(surface.Vertices.Count * 3), 8, writer =>
        {
            foreach (var vertex in surface.Vertices)
            {
                writer.Write(vertex[0]);
                writer.Write(vertex[1]);
                writer.Write(vertex[2]);
            }
        }));
        fbx.Value("PolygonVertexIndex", new FbxArray((byte)'i', checked(surface.Faces.Count * 3), 4, writer =>
        {
            foreach (var face in surface.Faces)
            {
                writer.Write(face[0]);
                writer.Write(face[1]);
                writer.Write(~face[2]);
            }
        }));

        fbx.Node("LayerElementUV", () =>
        {
            fbx.Value("Version", 101);
            fbx.Value("Name", "UVMap");
            fbx.Value("MappingInformationType", "ByVertice");
            fbx.Value("ReferenceInformationType", "Direct");
            fbx.Value("UV", new FbxArray((byte)'d', checked(uvs.Count * 2), 8, writer =>
            {
                foreach (var uv in uvs)
                {
                    writer.Write(uv[0]);
                    writer.Write(1 - uv[1]);
                }
            }));
        }, 0);
        fbx.Node("LayerElementMaterial", () =>
        {
            fbx.Value("Version", 101);
            fbx.Value("Name", "");
            fbx.Value("MappingInformationType", "AllSame");
            fbx.Value("ReferenceInformationType", "IndexToDirect");
            fbx.Value("Materials", new FbxArray((byte)'i', 1, 4, writer => writer.Write(0)));
        }, 0);
        fbx.Node("Layer", () =>
        {
            fbx.Value("Version", 100);
            WriteLayer(fbx, "LayerElementUV");
            WriteLayer(fbx, "LayerElementMaterial");
        }, 0);
    }

    private static void WriteLayer(FbxNodeWriter fbx, string type)
    {
        fbx.Node("LayerElement", () =>
        {
            fbx.Value("Type", type);
            fbx.Value("TypedIndex", 0);
        });
    }

    private static void WriteMaterial(FbxNodeWriter fbx, long material, long texture, long video)
    {
        const string textureName = "Atlas\0\u0001Texture";
        const string videoName = "Atlas\0\u0001Video";
        const string atlas = "atlas.jpg";

        fbx.Node("Material", () =>
        {
            fbx.Value("Version", 102);
            fbx.Value("ShadingModel", "Lambert");
            fbx.Value("MultiLayer", 0);
            fbx.Node("Properties70", () => fbx.Value("P", "DiffuseColor", "Color", "", "A", 1d, 1d, 1d));
        }, material, "Cliff\0\u0001Material", "");
        fbx.Node("Texture", () =>
        {
            fbx.Value("Type", "TextureVideoClip");
            fbx.Value("Version", 202);
            fbx.Value("TextureName", textureName);
            fbx.Value("Media", videoName);
            fbx.Value("FileName", atlas);
            fbx.Value("RelativeFilename", atlas);
        }, texture, textureName, "");
        fbx.Node("Video", () =>
        {
            fbx.Value("Type", "Clip");
            fbx.Value("Filename", atlas);
            fbx.Value("RelativeFilename", atlas);
        }, video, videoName, "Clip");
    }
}

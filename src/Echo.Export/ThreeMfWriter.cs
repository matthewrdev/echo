using System.IO.Compression;
using System.Xml;
using Echo.Geometry;
using Echo.Models;

namespace Echo.Export;

public sealed class ThreeMfWriter : IPrintMeshWriter
{
    private const string ModelNamespace = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    public void Write(string path, Surface surface, double sizeMillimetres)
    {
        if (!double.IsFinite(sizeMillimetres) || sizeMillimetres <= 0)
        {
            throw new EchoException("3MF requires a positive longest dimension in millimetres.", 2);
        }

        var vertices = new List<V3>();
        var indices = new Dictionary<V3, int>();
        var remap = new int[surface.Vertices.Count];
        var index = 0;
        foreach (var vertex in surface.Vertices)
        {
            // Mesh checkpoints can split coincident vertices for material seams.
            // Use the same single-precision positions as the mesh PLY artifact.
            var position = new V3((float)vertex[0], (float)vertex[1], (float)vertex[2]);
            if (!indices.TryGetValue(position, out var existing))
            {
                existing = vertices.Count;
                vertices.Add(position);
                indices.Add(position, existing);
            }

            remap[index++] = existing;
        }

        var reverseWinding = ValidateHull(vertices, surface.Faces, remap);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WritePart(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>
            </Types>
            """);
        WritePart(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>
            </Relationships>
            """);
        using var output = archive.CreateEntry("3D/3dmodel.model").Open();
        using var xml = XmlWriter.Create(output);
        WriteModel(xml, vertices, surface.Faces, remap, sizeMillimetres, reverseWinding);
    }

    private void WriteModel(XmlWriter xml, IReadOnlyList<V3> vertices, IList<int[]> faces, int[] remap,
        double sizeMillimetres, bool reverseWinding)
    {
        var minX = vertices.Min(vertex => vertex.X);
        var minY = vertices.Min(vertex => vertex.Y);
        var minZ = vertices.Min(vertex => vertex.Z);
        var maxX = vertices.Max(vertex => vertex.X);
        var maxY = vertices.Max(vertex => vertex.Y);
        var maxZ = vertices.Max(vertex => vertex.Z);
        var span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        var scale = sizeMillimetres / span;
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new EchoException("The saved mesh has no finite printable extent.", 2);
        }

        xml.WriteStartDocument();
        xml.WriteStartElement("model", ModelNamespace);
        xml.WriteAttributeString("unit", "millimeter");
        xml.WriteStartElement("resources", ModelNamespace);
        xml.WriteStartElement("object", ModelNamespace);
        xml.WriteAttributeString("id", "1");
        xml.WriteAttributeString("type", "model");
        xml.WriteStartElement("mesh", ModelNamespace);
        xml.WriteStartElement("vertices", ModelNamespace);
        foreach (var vertex in vertices)
        {
            xml.WriteStartElement("vertex", ModelNamespace);
            xml.WriteAttributeString("x", XmlConvert.ToString((vertex.X - minX) * scale));
            xml.WriteAttributeString("y", XmlConvert.ToString((maxZ - vertex.Z) * scale));
            xml.WriteAttributeString("z", XmlConvert.ToString((vertex.Y - minY) * scale));
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteStartElement("triangles", ModelNamespace);
        foreach (var face in faces)
        {
            xml.WriteStartElement("triangle", ModelNamespace);
            xml.WriteAttributeString("v1", XmlConvert.ToString(remap[face[0]]));
            xml.WriteAttributeString("v2", XmlConvert.ToString(remap[face[reverseWinding ? 2 : 1]]));
            xml.WriteAttributeString("v3", XmlConvert.ToString(remap[face[reverseWinding ? 1 : 2]]));
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteStartElement("build", ModelNamespace);
        xml.WriteStartElement("item", ModelNamespace);
        xml.WriteAttributeString("objectid", "1");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private bool ValidateHull(IReadOnlyList<V3> vertices, IList<int[]> faces, int[] remap)
    {
        // Mark paired edges with zero so a third face cannot start another pair.
        var edges = new Dictionary<ulong, sbyte>();
        var volume = 0d;
        var origin = vertices.Count == 0 ? default : vertices[0];
        foreach (var face in faces)
        {
            var first = remap[face[0]];
            var second = remap[face[1]];
            var third = remap[face[2]];
            AddEdge(edges, first, second);
            AddEdge(edges, second, third);
            AddEdge(edges, third, first);
            var a = vertices[first] - origin;
            var b = vertices[second] - origin;
            var c = vertices[third] - origin;
            if ((b - a).Cross(c - a).Length == 0)
            {
                throw InvalidHull();
            }

            volume += a.Dot(b.Cross(c));
        }

        if (!double.IsFinite(volume) || volume == 0 || edges.Values.Any(value => value != 0))
        {
            throw InvalidHull();
        }

        return volume < 0;
    }

    private void AddEdge(Dictionary<ulong, sbyte> edges, int start, int end)
    {
        var key = ((ulong)(uint)Math.Min(start, end) << 32) | (uint)Math.Max(start, end);
        var direction = (sbyte)(start < end ? 1 : -1);
        if (!edges.TryGetValue(key, out var previous))
        {
            edges.Add(key, direction);
        }
        else if (previous == -direction)
        {
            edges[key] = 0;
        }
        else
        {
            throw InvalidHull();
        }
    }

    private EchoException InvalidHull()
        => new("3MF needs a closed, consistently oriented mesh with positive volume. Use mesh completion and Close hull, then check the saved mesh for open or non-manifold edges.", 2);

    private void WritePart(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }
}

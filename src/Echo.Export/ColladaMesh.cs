using Echo.Models;

namespace Echo.Export;

internal static class ColladaMesh
{
    public static void Write(TextWriter writer, Surface surface, IList<double[]> uvs)
    {
        writer.WriteLine("""
            <?xml version="1.0" encoding="utf-8"?>
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <asset><created>1970-01-01T00:00:00Z</created><modified>1970-01-01T00:00:00Z</modified>
                <unit name="meter" meter="1"/><up_axis>Y_UP</up_axis>
              </asset>
              <library_images><image id="atlas"><init_from>atlas.jpg</init_from></image></library_images>
              <library_effects><effect id="cliff-effect"><profile_COMMON>
                <newparam sid="surface"><surface type="2D"><init_from>atlas</init_from></surface></newparam>
                <newparam sid="sampler"><sampler2D><source>surface</source></sampler2D></newparam>
                <technique sid="common"><lambert><diffuse><texture texture="sampler" texcoord="UVMap"/></diffuse></lambert></technique>
              </profile_COMMON></effect></library_effects>
              <library_materials><material id="cliff-material" name="Cliff">
                <instance_effect url="#cliff-effect"/>
              </material></library_materials>
              <library_geometries><geometry id="cliff" name="Cliff"><mesh>
            """);
        WriteSource(writer, "positions", surface.Vertices, ["X", "Y", "Z"]);
        WriteSource(writer, "uvs", uvs, ["S", "T"]);
        writer.WriteLine("""
                  <vertices id="vertices"><input semantic="POSITION" source="#positions"/></vertices>
            """);
        writer.WriteLine(FormattableString.Invariant($"      <triangles count=\"{surface.Faces.Count}\" material=\"Cliff\">"));
        writer.WriteLine("""
                    <input semantic="VERTEX" source="#vertices" offset="0"/>
                    <input semantic="TEXCOORD" source="#uvs" offset="1" set="0"/>
                    <p>
            """);
        foreach (var face in surface.Faces)
        {
            foreach (var vertex in face)
            {
                writer.Write(FormattableString.Invariant($"{vertex} {vertex} "));
            }

            writer.WriteLine();
        }

        writer.WriteLine("""
                    </p>
                  </triangles>
                </mesh></geometry></library_geometries>
                <library_visual_scenes><visual_scene id="scene"><node id="Cliff" name="Cliff">
                  <instance_geometry url="#cliff"><bind_material><technique_common>
                    <instance_material symbol="Cliff" target="#cliff-material">
                      <bind_vertex_input semantic="UVMap" input_semantic="TEXCOORD" input_set="0"/>
                    </instance_material>
                  </technique_common></bind_material></instance_geometry>
                </node></visual_scene></library_visual_scenes>
                <scene><instance_visual_scene url="#scene"/></scene>
              </COLLADA>
            """);
    }

    private static void WriteSource(TextWriter writer, string id, IList<double[]> values, string[] axes)
    {
        writer.WriteLine(FormattableString.Invariant($"      <source id=\"{id}\"><float_array id=\"{id}-data\" count=\"{(long)values.Count * axes.Length}\">"));
        foreach (var value in values)
        {
            var coordinates = axes.Length == 2
                ? FormattableString.Invariant($"{value[0]:R} {1 - value[1]:R}")
                : FormattableString.Invariant($"{value[0]:R} {value[1]:R} {value[2]:R}");
            writer.WriteLine(coordinates);
        }

        writer.WriteLine(FormattableString.Invariant($"      </float_array><technique_common><accessor source=\"#{id}-data\" count=\"{values.Count}\" stride=\"{axes.Length}\">"));
        foreach (var axis in axes)
        {
            writer.WriteLine($"<param name=\"{axis}\" type=\"float\"/>");
        }

        writer.WriteLine("</accessor></technique_common></source>");
    }
}

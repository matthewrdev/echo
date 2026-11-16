using System.ComponentModel.Composition;
using System.Globalization;
using System.Text.Json;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Infrastructure;
using Echo.Metadata;

namespace Echo.Export;

[Export(typeof(IGisWriter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class GisWriter : IGisWriter
{
    private readonly IFileOperations files;

    [ImportingConstructor]
    public GisWriter(IFileOperations files)
    {
        this.files = files;
    }

    public void Write(string directory, ReconstructionScene scene, Surface? surface, IReadOnlyList<Photo> photos)
    {
        var origin = scene.Origin ?? throw new EchoException("This reconstruction has no geographic origin. Add placement hints and align it first.", 2);
        origin.Validate();
        if (scene.Frame.Contains("arbitrary", StringComparison.OrdinalIgnoreCase))
        {
            throw new EchoException("An arbitrary-scale reconstruction cannot be exported as geographic geometry.", 2);
        }

        Directory.CreateDirectory(directory);
        var height = GeographicCoordinates.HasEllipsoidalHeight(origin);
        files.AtomicJson(Path.Combine(directory, "georeference.json"), new GisReference
        {
            Frame = scene.Frame,
            Origin = origin,
            LocalToEcefRowMajor = CameraGeometry.LocalToEcefMatrix(origin),
            GeometryHasEllipsoidalHeight = height,
            VerticalNote = height ? "GeoJSON Z is WGS84 ellipsoidal height in metres."
                : "GeoJSON geometry is horizontal only. Local up and source elevation are retained in attributes and vertices.csv; they are not verified WGS84 ellipsoidal heights.",
            Warnings = scene.Warnings
        });
        WriteCameras(Path.Combine(directory, "cameras.geojson"), scene, photos, origin, height);
        WritePhotoLocations(Path.Combine(directory, "photo-locations.geojson"), photos);
        if (surface != null)
        {
            WriteSurface(Path.Combine(directory, "model.geojson"), surface, origin, height);
            WriteVertices(Path.Combine(directory, "vertices.csv"), surface, origin);
        }

        File.WriteAllText(Path.Combine(directory, "QGIS.txt"), """
            Echo geographic export

            In QGIS, drag cameras.geojson and model.geojson into the map. Their horizontal
            coordinates are WGS84 longitude/latitude (EPSG:4326 in QGIS). Each model feature
            is one mesh triangle; this is geometry, not a textured raster or an orthophoto.
            Vertical faces may collapse to lines in the horizontal map.

            photo-locations.geojson contains the original GPS positions for all located
            photographs, including those not aligned. photo_altitude_m retains the recorded
            altitude; relative_altitude_m retains the height above takeoff when present.
            altitude_source, vertical_datum and altitude_reference_evidence explain the
            reference inference. These observations are distinct from reconstructed cameras.

            When exported from Echo's geographic viewer, model-map.png, model-map.pgw,
            model-map.prj and model-map.png.aux.xml provide a transparent, top-down textured model rendering. Keep
            these files together, add the PNG as a raster and assign EPSG:3857 if prompted.
            This is a model projection, not a surveyed orthophoto. Apple imagery is not exported.

            Read georeference.json before using elevations. GeoJSON has Z coordinates only
            when the source explicitly identifies WGS84 ellipsoidal heights. Otherwise its
            geometry is 2D; no sea-level or ground elevation is invented. vertices.csv retains
            every local XYZ coordinate, horizontal location and available source elevation.
            Triangle attributes a/b/c reference vertex_id in that CSV, preserving connectivity.
            In QGIS, load vertices.csv as delimited text using longitude/latitude and EPSG:4326.
            For ellipsoidal 3D GeoJSON, enable a 3D renderer in layer properties, use absolute
            elevation from geometry, and use a suitable projected project CRS for the site.
            Unknown or non-ellipsoidal heights need an explicitly chosen vertical transformation
            before they can be compared with terrain. Local up is relative to the model origin.

            If available, cliff.glb and cliff.geo.json preserve Echo's textured model and
            original transform. QGIS does not automatically apply Echo's GLB sidecar.
            Geographic placement inherits the accuracy of the photographs and placement hints.
            """);
    }

    private void WritePhotoLocations(string path, IReadOnlyList<Photo> photos)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream);
        StartCollection(writer);
        foreach (var photo in photos)
        {
            if (photo.Gps == null)
            {
                continue;
            }
            photo.Gps.Validate();
            var reference = AltitudeReferenceResolver.Resolve(photo.Gps, photo.Metadata);
            var position = new GeoPosition
            {
                Latitude = photo.Gps.Latitude,
                Longitude = photo.Gps.Longitude,
                Altitude = photo.Gps.Altitude,
                AltitudeDatum = reference.Datum
            };
            StartFeature(writer, "Point");
            Coordinate(writer, position, GeographicCoordinates.HasEllipsoidalHeight(position));
            writer.WriteEndObject();
            writer.WriteStartObject("properties");
            writer.WriteString("image_id", photo.Id);
            writer.WriteString("name", photo.Name);
            writer.WriteString("altitude_source", reference.Source);
            writer.WriteString("vertical_datum", reference.Datum);
            writer.WriteString("altitude_reference_evidence", reference.Evidence);
            NullableNumber(writer, "photo_altitude_m", photo.Gps.Altitude);
            NullableNumber(writer, "absolute_altitude_m", AltitudeReferenceResolver.Number(photo.Metadata, "AbsoluteAltitude"));
            NullableNumber(writer, "gps_altitude_m", AltitudeReferenceResolver.Number(photo.Metadata, "GPSAltitude"));
            NullableNumber(writer, "relative_altitude_m", AltitudeReferenceResolver.Number(photo.Metadata, "RelativeAltitude"));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        EndCollection(writer);
    }

    private void NullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(name, value.Value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private void WriteCameras(string path, ReconstructionScene scene, IReadOnlyList<Photo> photos, GeoPosition origin, bool height)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream);
        StartCollection(writer);
        var names = photos.ToDictionary(photo => photo.Id, photo => photo.Name);
        foreach (var camera in scene.Cameras)
        {
            var point = Position(camera.Centre, origin);
            StartFeature(writer, "Point");
            Coordinate(writer, point, height);
            writer.WriteEndObject();
            writer.WriteStartObject("properties");
            writer.WriteString("image_id", camera.ImageId);
            writer.WriteString("name", names.GetValueOrDefault(camera.ImageId, camera.ImageId));
            writer.WriteString("source", camera.Source);
            writer.WriteNumber("local_up_m", camera.Centre[1]);
            Elevation(writer, point);
            writer.WriteStartArray("camera_to_world_rotation");
            foreach (var value in camera.Rotation)
            {
                writer.WriteNumberValue(value);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        EndCollection(writer);
    }

    private void WriteSurface(string path, Surface surface, GeoPosition origin, bool height)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream);
        StartCollection(writer);
        for (var index = 0; index < surface.Faces.Count; index++)
        {
            var face = surface.Faces[index];
            if (face.Length != 3 || face.Any(vertex => vertex < 0 || vertex >= surface.Vertices.Count))
            {
                throw new EchoException("Invalid triangle in geographic export.", 2);
            }
            var points = face.Select(vertex => Position(surface.Vertices[vertex], origin)).ToArray();
            if (points.Max(point => point.Longitude) - points.Min(point => point.Longitude) > 180)
            {
                throw new EchoException("This model crosses the antimeridian; split it before geographic export.", 2);
            }
            // RFC 7946 exterior rings run counterclockwise in longitude/latitude.
            var cross = (points[1].Longitude - points[0].Longitude) * (points[2].Latitude - points[0].Latitude)
                - (points[2].Longitude - points[0].Longitude) * (points[1].Latitude - points[0].Latitude);
            var order = cross < 0 ? new[] { 0, 2, 1, 0 } : new[] { 0, 1, 2, 0 };
            StartFeature(writer, "Polygon");
            writer.WriteStartArray();
            writer.WriteStartArray();
            foreach (var vertex in order)
            {
                Coordinate(writer, points[vertex], height);
            }
            writer.WriteEndArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteStartObject("properties");
            writer.WriteNumber("triangle_id", index);
            writer.WriteNumber("a", face[0]);
            writer.WriteNumber("b", face[1]);
            writer.WriteNumber("c", face[2]);
            writer.WriteNumber("local_up_min_m", face.Min(vertex => surface.Vertices[vertex][1]));
            writer.WriteNumber("local_up_max_m", face.Max(vertex => surface.Vertices[vertex][1]));
            writer.WriteString("vertical_datum", origin.AltitudeDatum);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        EndCollection(writer);
    }

    private void WriteVertices(string path, Surface surface, GeoPosition origin)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("vertex_id,longitude,latitude,local_east_m,local_up_m,local_south_m,source_elevation_m");
        for (var index = 0; index < surface.Vertices.Count; index++)
        {
            var local = surface.Vertices[index];
            var point = Position(local, origin);
            writer.WriteLine(string.Join(",", new double?[] { index, point.Longitude, point.Latitude, local[0], local[1], local[2], point.Altitude }
                .Select(value => value?.ToString("R", CultureInfo.InvariantCulture) ?? "")));
        }
    }

    private GeoPosition Position(double[] values, GeoPosition origin)
    {
        if (values.Length != 3 || values.Any(value => !double.IsFinite(value)))
        {
            throw new EchoException("Invalid local coordinate in geographic export.", 2);
        }
        return GeographicCoordinates.FromLocal(new V3(values), origin);
    }

    private void StartCollection(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "FeatureCollection");
        writer.WriteStartArray("features");
    }

    private void EndCollection(Utf8JsonWriter writer)
    {
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private void StartFeature(Utf8JsonWriter writer, string geometry)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");
        writer.WriteStartObject("geometry");
        writer.WriteString("type", geometry);
        writer.WritePropertyName("coordinates");
    }

    private void Coordinate(Utf8JsonWriter writer, GeoPosition point, bool height)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(point.Longitude);
        writer.WriteNumberValue(point.Latitude);
        if (height)
        {
            writer.WriteNumberValue(point.Altitude!.Value);
        }
        writer.WriteEndArray();
    }

    private void Elevation(Utf8JsonWriter writer, GeoPosition point)
    {
        writer.WriteString("vertical_datum", point.AltitudeDatum);
        if (point.Altitude.HasValue)
        {
            writer.WriteNumber("source_elevation_m", point.Altitude.Value);
        }
        else
        {
            writer.WriteNull("source_elevation_m");
        }
    }
}

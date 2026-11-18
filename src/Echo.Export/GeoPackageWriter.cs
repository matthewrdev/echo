using System.ComponentModel.Composition;
using System.Globalization;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Metadata;
using Microsoft.Data.Sqlite;

namespace Echo.Export;

[Export(typeof(IGeoPackageWriter)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class GeoPackageWriter : IGeoPackageWriter
{
    [ImportingConstructor]
    public GeoPackageWriter()
    {
    }

    public void Write(string path, ReconstructionScene scene, Surface? surface, IReadOnlyList<Photo> photos)
    {
        var origin = scene.Origin ?? throw new EchoException("GeoPackage requires a geographic origin.", 2);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        Execute(connection, """
            PRAGMA application_id = 1196444487;
            PRAGMA user_version = 10400;
            PRAGMA foreign_keys = ON;
            CREATE TABLE gpkg_spatial_ref_sys (
                srs_name TEXT NOT NULL, srs_id INTEGER NOT NULL PRIMARY KEY,
                organization TEXT NOT NULL, organization_coordsys_id INTEGER NOT NULL,
                definition TEXT NOT NULL, description TEXT);
            INSERT INTO gpkg_spatial_ref_sys VALUES
                ('Undefined Cartesian', -1, 'NONE', -1, 'undefined', 'Undefined Cartesian coordinate reference system'),
                ('Undefined Geographic', 0, 'NONE', 0, 'undefined', 'Undefined geographic coordinate reference system'),
                ('WGS 84', 4326, 'EPSG', 4326,
                'GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433],AUTHORITY["EPSG","4326"]]',
                'Horizontal longitude/latitude. Heights and local model coordinates are attributes.');
            CREATE TABLE gpkg_contents (
                table_name TEXT NOT NULL PRIMARY KEY, data_type TEXT NOT NULL, identifier TEXT UNIQUE,
                description TEXT DEFAULT '', last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                min_x DOUBLE, min_y DOUBLE, max_x DOUBLE, max_y DOUBLE, srs_id INTEGER,
                FOREIGN KEY(srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id));
            CREATE TABLE gpkg_geometry_columns (
                table_name TEXT NOT NULL, column_name TEXT NOT NULL, geometry_type_name TEXT NOT NULL,
                srs_id INTEGER NOT NULL, z TINYINT NOT NULL, m TINYINT NOT NULL,
                PRIMARY KEY(table_name,column_name), UNIQUE(table_name),
                FOREIGN KEY(table_name) REFERENCES gpkg_contents(table_name),
                FOREIGN KEY(srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id));
            """);
        using var transaction = connection.BeginTransaction();
        var names = photos.ToDictionary(photo => photo.Id, photo => photo.Name);
        WriteLayer(connection, "cameras", false, scene.Cameras.Select(camera => new GeoPackageFeature
        {
            Coordinates = [GeographicCoordinates.FromLocal(new V3(camera.Centre), origin)],
            SourceId = camera.ImageId,
            Name = names.GetValueOrDefault(camera.ImageId, camera.ImageId),
            Source = camera.Source,
            Local = camera.Centre
        }));
        WriteLayer(connection, "photo_locations", false, PhotoFeatures(photos));
        if (surface != null)
        {
            WriteLayer(connection, "vertices", false, surface.Vertices.Select((vertex, index) => new GeoPackageFeature
            {
                SourceId = index.ToString(CultureInfo.InvariantCulture),
                Coordinates = [GeographicCoordinates.FromLocal(new V3(vertex), origin)],
                Local = vertex
            }));
            WriteLayer(connection, "model", true, surface.Faces.Select((face, index) => new GeoPackageFeature
            {
                SourceId = index.ToString(CultureInfo.InvariantCulture),
                Coordinates = face.Select(vertex => GeographicCoordinates.FromLocal(new V3(surface.Vertices[vertex]), origin)).ToArray(),
                Vertices = face
            }));
        }
        transaction.Commit();
    }

    private IEnumerable<GeoPackageFeature> PhotoFeatures(IReadOnlyList<Photo> photos)
    {
        foreach (var photo in photos.Where(photo => photo.Gps != null))
        {
            var reference = AltitudeReferenceResolver.Resolve(photo.Gps!, photo.Metadata);
            yield return new GeoPackageFeature
            {
                SourceId = photo.Id,
                Name = photo.Name,
                Source = reference.Source,
                Evidence = reference.Evidence,
                Coordinates = [new GeoPosition
                {
                    Latitude = photo.Gps!.Latitude,
                    Longitude = photo.Gps.Longitude,
                    Altitude = photo.Gps.Altitude,
                    AltitudeDatum = reference.Datum
                }]
            };
        }
    }

    private void WriteLayer(SqliteConnection connection, string table, bool polygon, IEnumerable<GeoPackageFeature> features)
    {
        // A vertical triangle can become a line or point in horizontal geography.
        var geometryType = polygon ? "GEOMETRY" : "POINT";
        // Table names are fixed by this writer, never supplied by the project.
        Execute(connection, $"""
            CREATE TABLE {table} (
                fid INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, geom {geometryType}, source_id TEXT, name TEXT, source TEXT,
                local_east_m REAL, local_up_m REAL, local_south_m REAL,
                source_elevation_m REAL, vertical_datum TEXT, altitude_reference_evidence TEXT, a INTEGER, b INTEGER, c INTEGER);
            INSERT INTO gpkg_contents(table_name,data_type,identifier,srs_id) VALUES ('{table}','features','{table}',4326);
            INSERT INTO gpkg_geometry_columns VALUES ('{table}','geom','{geometryType}',4326,0,0);
            """);
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table}(geom,source_id,name,source,local_east_m,local_up_m,local_south_m,source_elevation_m,vertical_datum,altitude_reference_evidence,a,b,c) VALUES ($geom,$id,$name,$source,$x,$y,$z,$height,$datum,$evidence,$a,$b,$c)";
        foreach (var parameter in new[] { "$geom", "$id", "$name", "$source", "$x", "$y", "$z", "$height", "$datum", "$evidence", "$a", "$b", "$c" })
        {
            command.Parameters.AddWithValue(parameter, DBNull.Value);
        }
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var feature in features)
        {
            var point = feature.Coordinates[0];
            command.Parameters["$geom"].Value = Geometry(feature.Coordinates, polygon);
            command.Parameters["$id"].Value = feature.SourceId;
            command.Parameters["$name"].Value = feature.Name;
            command.Parameters["$source"].Value = feature.Source;
            command.Parameters["$x"].Value = (object?)feature.Local?[0] ?? DBNull.Value;
            command.Parameters["$y"].Value = (object?)feature.Local?[1] ?? DBNull.Value;
            command.Parameters["$z"].Value = (object?)feature.Local?[2] ?? DBNull.Value;
            command.Parameters["$height"].Value = polygon ? DBNull.Value : (object?)point.Altitude ?? DBNull.Value;
            command.Parameters["$datum"].Value = point.AltitudeDatum;
            command.Parameters["$evidence"].Value = feature.Evidence;
            command.Parameters["$a"].Value = (object?)feature.Vertices?[0] ?? DBNull.Value;
            command.Parameters["$b"].Value = (object?)feature.Vertices?[1] ?? DBNull.Value;
            command.Parameters["$c"].Value = (object?)feature.Vertices?[2] ?? DBNull.Value;
            command.ExecuteNonQuery();
            foreach (var coordinate in feature.Coordinates)
            {
                minX = Math.Min(minX, coordinate.Longitude);
                maxX = Math.Max(maxX, coordinate.Longitude);
                minY = Math.Min(minY, coordinate.Latitude);
                maxY = Math.Max(maxY, coordinate.Latitude);
            }
        }
        if (double.IsFinite(minX))
        {
            using var extent = connection.CreateCommand();
            extent.CommandText = "UPDATE gpkg_contents SET min_x=$minX,min_y=$minY,max_x=$maxX,max_y=$maxY WHERE table_name=$table";
            extent.Parameters.AddWithValue("$minX", minX);
            extent.Parameters.AddWithValue("$minY", minY);
            extent.Parameters.AddWithValue("$maxX", maxX);
            extent.Parameters.AddWithValue("$maxY", maxY);
            extent.Parameters.AddWithValue("$table", table);
            extent.ExecuteNonQuery();
        }
    }

    private byte[] Geometry(GeoPosition[] points, bool polygon)
    {
        uint geometryType = polygon ? 3u : 1u;
        if (polygon)
        {
            var cross = (points[1].Longitude - points[0].Longitude) * (points[2].Latitude - points[0].Latitude)
                - (points[2].Longitude - points[0].Longitude) * (points[1].Latitude - points[0].Latitude);
            if (Math.Abs(cross) < 1e-18)
            {
                points = points.DistinctBy(point => new GeoPositionKey(point.Longitude, point.Latitude)).ToArray();
                geometryType = points.Length == 1 ? 1u : 2u;
                polygon = false;
            }
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 0x47, 0x50, 0, 1 }); // GeoPackage v0, little endian, no envelope.
        writer.Write(4326);
        writer.Write((byte)1);
        writer.Write(geometryType);
        if (polygon)
        {
            writer.Write(1u);
            writer.Write((uint)points.Length + 1);
        }
        else if (geometryType == 2)
        {
            writer.Write((uint)points.Length);
        }
        foreach (var point in points)
        {
            writer.Write(point.Longitude);
            writer.Write(point.Latitude);
        }
        if (polygon)
        {
            writer.Write(points[0].Longitude);
            writer.Write(points[0].Latitude);
        }
        return stream.ToArray();
    }

    private void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

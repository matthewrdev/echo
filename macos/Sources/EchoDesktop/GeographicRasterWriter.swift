import AppKit
import MapKit

struct GeographicRasterWriter {
    func write(_ model: ModelMapOverlay, directory: URL) throws {
        guard let tiff = model.image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
            let png = bitmap.representation(using: .png, properties: [:])
        else { throw CLIError(message: "Cannot encode the geographic model image.") }
        let coefficients = worldFile(rect: model.boundingMapRect, width: bitmap.pixelsWide, height: bitmap.pixelsHigh)
        let text =
            coefficients.map { String(format: "%.12f", locale: Locale(identifier: "en_US_POSIX"), $0) }.joined(
                separator: "\n") + "\n"
        try png.write(to: directory.appendingPathComponent("model-map.png"), options: .atomic)
        try text.write(to: directory.appendingPathComponent("model-map.pgw"), atomically: true, encoding: .utf8)
        let projection = """
            PROJCS["WGS 84 / Pseudo-Mercator",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]],PROJECTION["Mercator_1SP"],PARAMETER["central_meridian",0],PARAMETER["scale_factor",1],PARAMETER["false_easting",0],PARAMETER["false_northing",0],UNIT["metre",1],EXTENSION["PROJ4","+proj=merc +a=6378137 +b=6378137 +lat_ts=0 +lon_0=0 +x_0=0 +y_0=0 +k=1 +units=m +nadgrids=@null +wktext +no_defs"],AUTHORITY["EPSG","3857"]]
            """
        try projection.write(to: directory.appendingPathComponent("model-map.prj"), atomically: true, encoding: .utf8)
        // GDAL/QGIS reads the CRS from PAM for PNGs; a world file alone contains no CRS.
        let transform = [
            coefficients[4] - coefficients[0] / 2, coefficients[0], 0,
            coefficients[5] - coefficients[3] / 2, 0, coefficients[3],
        ]
        .map { String(format: "%.12f", locale: Locale(identifier: "en_US_POSIX"), $0) }.joined(separator: ", ")
        let auxiliary = """
            <PAMDataset><SRS dataAxisToSRSAxisMapping="1,2">\(projection)</SRS><GeoTransform>\(transform)</GeoTransform></PAMDataset>
            """
        try auxiliary.write(
            to: directory.appendingPathComponent("model-map.png.aux.xml"), atomically: true, encoding: .utf8)
    }

    func worldFile(rect: MKMapRect, width: Int, height: Int) -> [Double] {
        let circumference = 2 * Double.pi * 6_378_137
        let scale = circumference / MKMapRect.world.width
        let pixelX = rect.width * scale / Double(width)
        let pixelY = -rect.height * scale / Double(height)
        return [
            pixelX, 0, 0, pixelY, rect.minX * scale - circumference / 2 + pixelX / 2,
            circumference / 2 - rect.minY * scale + pixelY / 2,
        ]
    }
}

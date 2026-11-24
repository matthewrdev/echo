enum SurveyExportCoordinates: String, CaseIterable, Identifiable {
    case local = "Local (Z up)"
    case earthCentred = "WGS84 Earth-centred (EPSG:4978)"
    case projected = "Projected EPSG code"

    var id: String { rawValue }
}

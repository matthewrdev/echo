namespace Echo.Export;

public sealed record MappingProducts(MappingGrid Grid, double SourceOrthophotoPixelMetres,
    double OrthophotoAlphaCoveragePercent, double DsmCoveragePercent, double DtmCoveragePercent);

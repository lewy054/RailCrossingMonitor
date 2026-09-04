using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace RailCrossingMonitor.Application.Crossing;

public class RailwayCrossingService(HttpClient httpClient, IOptions<RailCrossingServiceOptions> options)
{
    public async Task<List<RailwayCrossing>> GetCrossingsAsync(CancellationToken cancellationToken)
    {
        var url = $"{options.Value.Url}?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetFeature&TYPENAMES=ms:PKP_PLK&SRSNAME=EPSG:4326";
        var xml = await httpClient.GetStringAsync(url, cancellationToken);
        return ParseXml(xml);
    }

    private static List<RailwayCrossing> ParseXml(string xml)
    {
        var document = XDocument.Parse(xml);

        XNamespace ms = "http://mapserver.gis.umn.edu/mapserver";
        XNamespace gml = "http://www.opengis.net/gml/3.2";

        var result = new List<RailwayCrossing>();

        foreach (var crossing in document
                     .Descendants(ms + "PKP_PLK"))
        {
            var pos = crossing
                .Descendants(gml + "Point")
                .Descendants(gml + "pos")
                .FirstOrDefault()?.Value;

            if (string.IsNullOrWhiteSpace(pos))
                continue;

            var coordinates = pos
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (coordinates.Length < 2)
                continue;

            // GML: latitude longitude
            if (!double.TryParse(
                    coordinates[0],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var latitude))
                continue;

            if (!double.TryParse(
                    coordinates[1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var longitude))
                continue;

            result.Add(new RailwayCrossing
            {
                Id = crossing.Element(ms + "GML_ID")?.Value ?? "",
                Name = crossing.Element(ms + "NAZWA")?.Value ?? "",
                Category = crossing.Element(ms + "KATEGORIA")?.Value ?? "",
                StationRoute = crossing.Element(ms + "STAC_SZLAK")?.Value ?? "",
                Manager = crossing.Element(ms + "IDDE")?.Value ?? "",
                Latitude = latitude,
                Longitude = longitude
            });
        }

        return result;
    }
}
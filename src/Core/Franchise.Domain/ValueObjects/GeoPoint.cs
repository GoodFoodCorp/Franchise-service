using Franchise.Domain.Errors;

namespace Franchise.Domain.ValueObjects;

/// <summary>A WGS84 position.</summary>
public readonly record struct GeoPoint
{
    private const double EarthRadiusKm = 6371.0088;

    public GeoPoint(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180 || double.IsNaN(latitude) || double.IsNaN(longitude))
        {
            throw DomainException.Validation("Coordinates are out of range.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    /// <summary>Great-circle distance (haversine), in kilometres.</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(other.Latitude - Latitude);
        var dLng = Rad(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(Rad(Latitude)) * Math.Cos(Rad(other.Latitude)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

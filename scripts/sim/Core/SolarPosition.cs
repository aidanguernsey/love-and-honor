namespace LoveAndHonor.Sim.Core;

/// <summary>
/// Sun elevation/azimuth for a place, day of year and local standard time (NOAA general solar position
/// formulas; accurate to well under a degree, plenty for lighting). Daylight saving time is not modelled.
/// </summary>
public static class SolarPosition
{
    /// <param name="latitudeDeg">North positive.</param>
    /// <param name="longitudeDeg">East positive (Oxford, OH ≈ −84.73).</param>
    /// <param name="dayOfYear">1–365.</param>
    /// <param name="localHour">Local standard time, 0–24.</param>
    /// <param name="utcOffsetHours">Standard-time UTC offset (Eastern = −5).</param>
    /// <returns>Elevation above the horizon and azimuth clockwise from true north, both in degrees.</returns>
    public static (double ElevationDeg, double AzimuthDeg) Compute(double latitudeDeg, double longitudeDeg, int dayOfYear, double localHour, double utcOffsetHours)
    {
        double gamma = 2 * Math.PI / 365.0 * (dayOfYear - 1 + (localHour - 12) / 24.0);
        double eqTimeMin = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                     - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                      - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                      - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

        double timeOffsetMin = eqTimeMin + 4 * longitudeDeg - 60 * utcOffsetHours;
        double trueSolarMin = localHour * 60 + timeOffsetMin;
        double hourAngle = (trueSolarMin / 4 - 180) * Math.PI / 180;

        double lat = latitudeDeg * Math.PI / 180;
        double cosZenith = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(hourAngle);
        double zenith = Math.Acos(Math.Clamp(cosZenith, -1, 1));
        double azimuth = Math.Atan2(Math.Sin(hourAngle), Math.Cos(hourAngle) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat)) * 180 / Math.PI + 180;
        return (90 - zenith * 180 / Math.PI, (azimuth % 360 + 360) % 360);
    }

    /// <summary>Unit vector pointing TOWARD the sun in map space: +X east, +Y up, +Z south.</summary>
    public static (float X, float Y, float Z) Direction(double elevationDeg, double azimuthDeg)
    {
        double el = elevationDeg * Math.PI / 180, az = azimuthDeg * Math.PI / 180;
        return ((float)(Math.Sin(az) * Math.Cos(el)), (float)Math.Sin(el), (float)(-Math.Cos(az) * Math.Cos(el)));
    }
}

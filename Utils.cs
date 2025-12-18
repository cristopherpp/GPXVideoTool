using System;

namespace GPXVideoTools
{
    public static class Utils
    {
        public static void ParseZoneString(string zoneStr, out int zone, out bool north)
        {
            north = zoneStr.EndsWith("N", System.StringComparison.OrdinalIgnoreCase);
            zone = System.Int32.Parse(zoneStr.Substring(0, zoneStr.Length - 1));
        }

        public static void LatLonToUtm(double lat, double lon, int zone, bool north, out double easting, out double northing, out int outZone, out bool outNorth)
        {
            const double a = 6378137;
            // const double b = 6356752.314;
            const double e2 = 6.69437999013e-3;
            double k0 = 0.9996;
            double lon0 = zone * 6 - 183;
            double phi = lat * System.Math.PI / 180;
            double lambda = lon * System.Math.PI / 180;
            double lambda0 = lon0 * System.Math.PI / 180;
            double N = a / System.Math.Sqrt(1 - e2 * System.Math.Sin(phi) * System.Math.Sin(phi));
            double T = System.Math.Tan(phi) * System.Math.Tan(phi);
            double C = e2 / (1 - e2) * System.Math.Cos(phi) * System.Math.Cos(phi);
            double A = (lambda - lambda0) * System.Math.Cos(phi);
            double M = a * ((1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * phi - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * System.Math.Sin(2 * phi) + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * System.Math.Sin(4 * phi) - (35 * e2 * e2 * e2 / 3072) * System.Math.Sin(6 * phi));
            easting = k0 * N * (A + (1 - T + C) * A * A * A / 6 + (5 - 18 * T + T * T + 72 * C - 58 * e2) * A * A * A * A * A / 120) + 500000;
            northing = k0 * (M + N * System.Math.Tan(phi) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * A * A * A * A / 24 + (61 - 58 * T + T * T + 600 * C - 330 * e2) * A * A * A * A * A * A / 720));
            if (!north) northing += 10000000;
            outZone = zone;
            outNorth = north;
        }

        public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            double r = 6371; // Radio Tierra en km
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * r * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }
    }
}
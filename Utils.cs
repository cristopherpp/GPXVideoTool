using System;

namespace GPXVideoTools
{
    public static class Utils
    {
        public static double Deg2Rad(double d) => d * Math.PI / 180.0;

        public static void ParseZoneString(string s, out int zoneNumber, out bool north)
        {
            zoneNumber = 17; north = false;
            if (string.IsNullOrEmpty(s)) return;
            s = s.Trim().ToUpper();
            char hemi = s[s.Length - 1];
            string num = s.Substring(0, s.Length - 1);
            int zn; if (int.TryParse(num, out zn)) zoneNumber = zn;
            north = (hemi == 'N');
        }

        public static void LatLonToUtm(double lat, double lon, int zoneOverride, bool forceNorth, out double easting, out double northing, out int zoneUsed, out bool northHem)
        {
            zoneUsed = (zoneOverride >= 1 && zoneOverride <= 60) ? zoneOverride : (int)Math.Floor((lon + 180) / 6) + 1;
            northHem = forceNorth;
            double a = 6378137.0;
            double f = 1.0 / 298.257223563;
            double k0 = 0.9996;

            double lonOrigin = (zoneUsed - 1) * 6 - 180 + 3;
            double latRad = Deg2Rad(lat);
            double lonRad = Deg2Rad(lon);
            double lonOriginRad = Deg2Rad(lonOrigin);

            double e = Math.Sqrt(1 - Math.Pow(1 - f, 2));
            double N = a / Math.Sqrt(1 - Math.Pow(e * Math.Sin(latRad), 2));
            double T = Math.Pow(Math.Tan(latRad), 2);
            double C = Math.Pow(e * Math.Cos(latRad) / Math.Sqrt(1 - e * e), 2);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1 - Math.Pow(e,2)/4 - 3*Math.Pow(e,4)/64 - 5*Math.Pow(e,6)/256) * latRad
                - (3*Math.Pow(e,2)/8 + 3*Math.Pow(e,4)/32 + 45*Math.Pow(e,6)/1024) * Math.Sin(2*latRad)
                + (15*Math.Pow(e,4)/256 + 45*Math.Pow(e,6)/1024) * Math.Sin(4*latRad)
                - (35*Math.Pow(e,6)/3072) * Math.Sin(6*latRad));

            double eastingTmp = k0 * N * (A + (1 - T + C) * Math.Pow(A,3)/6 + (5 - 18*T + T*T + 72*C - 58*Math.Pow(e,2)) * Math.Pow(A,5)/120) + 500000.0;
            double northingTmp = k0 * (M + N * Math.Tan(latRad) * (A*A/2 + (5 - T + 9*C + 4*C*C) * Math.Pow(A,4)/24 + (61 - 58*T + T*T + 600*C - 330*Math.Pow(e,2)) * Math.Pow(A,6)/720));

            if (lat < 0 && !forceNorth) northingTmp += 10000000.0;

            easting = eastingTmp; northing = northingTmp;
        }
    }
}

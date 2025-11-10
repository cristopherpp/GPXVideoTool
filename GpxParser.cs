using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace GPXVideoTools
{
    public class GpxPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double? Ele { get; set; }
        public DateTime Time { get; set; }
        public GpxPoint(double lat, double lon, DateTime time, double? ele = null) { Lat = lat; Lon = lon; Time = time; Ele = ele; }
    }

    public static class GpxParser
    {
        public static List<GpxPoint> Parse(string path)
        {
            var list = new List<GpxPoint>();
            var doc = XDocument.Load(path);
            XNamespace ns = doc.Root.GetDefaultNamespace();
            var trkpts = doc.Descendants(ns + "trkpt");
            if (!trkpts.Any()) trkpts = doc.Descendants("trkpt");

            foreach (var n in trkpts)
            {
                double lat = double.Parse(n.Attribute("lat").Value, CultureInfo.InvariantCulture);
                double lon = double.Parse(n.Attribute("lon").Value, CultureInfo.InvariantCulture);
                double? ele = null; var eleN = n.Element(ns + "ele") ?? n.Element("ele"); if (eleN != null) ele = double.Parse(eleN.Value, CultureInfo.InvariantCulture);
                DateTime t = DateTime.MinValue; var timeN = n.Element(ns + "time") ?? n.Element("time"); if (timeN != null) DateTime.TryParse(timeN.Value, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t);
                list.Add(new GpxPoint(lat, lon, t, ele));
            }
            return list;
        }
    }
}

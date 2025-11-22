using System;
using System.Collections.Generic;
using System.Xml;

namespace GPXVideoTools
{
    public static class GpxParser
    {
        public static System.Collections.Generic.List<GPXVideoTools.GpxPoint> Parse(string filePath)
        {
            var points = new System.Collections.Generic.List<GPXVideoTools.GpxPoint>();
            var xml = new System.Xml.XmlDocument();
            xml.Load(filePath);
            var ns = new System.Xml.XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("gpx", "http://www.topografix.com/GPX/1/1");
            var nodes = xml.SelectNodes("//gpx:trkpt", ns);
            if (nodes == null) return points;
            foreach (System.Xml.XmlNode node in nodes)
            {
                var p = new GPXVideoTools.GpxPoint
                {
                    Lat = double.Parse(node.Attributes["lat"].Value),
                    Lon = double.Parse(node.Attributes["lon"].Value),
                };
                var eleNode = node.SelectSingleNode("gpx:ele", ns);
                if (eleNode != null) p.Ele = double.Parse(eleNode.InnerText);
                var timeNode = node.SelectSingleNode("gpx:time", ns);
                if (timeNode != null) p.Time = System.DateTime.Parse(timeNode.InnerText);
                points.Add(p);
            }
            return points;
        }
    }
}
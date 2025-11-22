using System;

namespace GPXVideoTools
{
    public class GpxPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double? Ele { get; set; }
        public System.DateTime Time { get; set; }
    }
}
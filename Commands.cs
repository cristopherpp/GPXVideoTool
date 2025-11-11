using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Forms = System.Windows.Forms;

[assembly: CommandClass(typeof(GPXVideoTools.Commands))]

namespace GPXVideoTools
{
    public class Commands : IExtensionApplication
    {
        public static List<GpxPoint> TrackPoints = new List<GpxPoint>();
        public static double MarkerSize = 2.0;
        public static Color MarkerColor = Color.Red;
        public static Color RouteColor = Color.Blue;
        public static string SelectedUtmZone = "17S";

        private static GpxViewerForm _viewer;

        public void Initialize() { RibbonUI.CreateRibbon(); }
        public void Terminate() { }

        [CommandMethod("GPX_IMPORT_CMD")]
        public static void ImportAndOpen()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
            using (var ofd = new Forms.OpenFileDialog() { Filter = "GPX files (*.gpx)|*.gpx" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) { ed.WriteMessage("\nCancelado"); return; }
                var list = GpxParser.Parse(ofd.FileName);
                if (list == null || list.Count == 0) { ed.WriteMessage("\nNo hay puntos"); return; }
                TrackPoints = list;

                int zone; bool north; Utils.ParseZoneString(SelectedUtmZone, out zone, out north);

                var utm = new List<Point3d>();
                foreach (var p in TrackPoints) { double e, n; int zu; bool nh; Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out e, out n, out zu, out nh); utm.Add(new Point3d(e, n, p.Ele ?? 0.0)); }

                DrawPolyline(utm);

                bool hasEle = TrackPoints.Any(t => t.Ele.HasValue);
                var start = TrackPoints.FirstOrDefault(t => t.Time != null)?.Time;
                var end = TrackPoints.LastOrDefault(t => t.Time != null)?.Time;
                ed.WriteMessage($"\nZona UTM actual: {SelectedUtmZone} seleccionada.");
                ed.WriteMessage($"\nRuta importada: {TrackPoints.Count} puntos | Zona UTM {SelectedUtmZone} | Altitud: {(hasEle?"activada":"no disponible")} ");
                ed.WriteMessage($"\nInicio: {start:HH:mm:ss} | Fin: {end:HH:mm:ss}");

                var csvPath = Path.Combine(Path.GetDirectoryName(ofd.FileName), Path.GetFileNameWithoutExtension(ofd.FileName) + "_mapping.csv");
                ExportCsv(csvPath, TrackPoints, utm);
                ed.WriteMessage($"\nCSV generado: {csvPath}");

                if (_viewer == null || _viewer.IsDisposed) _viewer = new GpxViewerForm();
                _viewer.SetTrack(TrackPoints);
                _viewer.Show();
            }
        }

        private static void DrawPolyline(List<Point3d> pts)
        {
            if (pts == null || pts.Count == 0) return;
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument; var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var poly = new Polyline();
                for (int i = 0; i < pts.Count; i++) poly.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                poly.SetDatabaseDefaults(); poly.Color = Autodesk.AutoCAD.Colors.Color.FromColor(RouteColor);
                ms.AppendEntity(poly); tr.AddNewlyCreatedDBObject(poly, true);
                tr.Commit();
            }
        }

        // public static void ShowMarkerDialog() { using(var f=new MarkerSettingsForm(MarkerSize, MarkerColor)){ if(f.ShowDialog()==DialogResult.OK){ MarkerSize=f.MarkerSize; MarkerColor=f.MarkerColor; _viewer?.ApplyMarkerStyle(MarkerSize, MarkerColor); } } }
        public static void ShowRouteColorDialog() { using(var cd=new Forms.ColorDialog()){ cd.Color = RouteColor; if(cd.ShowDialog()== Forms.DialogResult.OK){ RouteColor = cd.Color; RibbonUI.UpdateRouteSwatch(RouteColor); UpdateRouteColorInDrawing(); } } }
        public static void ShowPlayPause() => _viewer?.PlayPause();
        public static void ShowPrev() => _viewer?.SeekBackward();
        public static void ShowNext() => _viewer?.SeekForward();
        public static void ToggleAutoSync() => _viewer?.ToggleAutoSync();

        private static void UpdateRouteColorInDrawing()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument; var db = doc.Database;
            using(var tr = db.TransactionManager.StartTransaction()){
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach(ObjectId id in ms){ var ent = tr.GetObject(id, OpenMode.ForRead) as Entity; if(ent is Polyline){ ent.UpgradeOpen(); ent.Color = Autodesk.AutoCAD.Colors.Color.FromColor(RouteColor); ent.DowngradeOpen(); break; } }
                tr.Commit();
            }
        }

        private static void ExportCsv(string path, List<GpxPoint> points, List<Point3d> utm)
        {
            using(var w = new StreamWriter(path)){
                w.WriteLine("Index,Latitude,Longitude,Easting,Northing,Altitude,Time,VideoSecond");
                DateTime baseT = points[0].Time;
                for(int i=0;i<points.Count;i++){
                    var p = points[i]; var u = utm[i]; double secs = (p.Time - baseT).TotalSeconds;
                    w.WriteLine($"{i},{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{u.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{u.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},{(p.Ele.HasValue?p.Ele.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"")},{p.Time.ToString("o")},{secs}");
                }
            }
        }
    }
}

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

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

        public void Initialize() => RibbonUI.CreateRibbon();
        public void Terminate() { }

        [CommandMethod("GPXTOOLS")]
        public static void LoadGpxTools()
        {
            RibbonUI.CreateRibbon();
            GpxPalette.Show();
        }

        [CommandMethod("GPX_IMPORT_CMD")]
        public static void ImportAndOpen()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            using (var ofd = new OpenFileDialog { Filter = "GPX files (*.gpx)|*.gpx" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) { ed.WriteMessage("\nCancelado"); return; }

                var list = GpxParser.Parse(ofd.FileName);
                if (list == null || list.Count == 0) { ed.WriteMessage("\nNo hay puntos"); return; }

                TrackPoints = list;

                int zone; bool north;
                Utils.ParseZoneString(SelectedUtmZone, out zone, out north);

                var utm = new List<Point3d>();
                foreach (var p in TrackPoints)
                {
                    Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out double e, out double n, out _, out _);
                    utm.Add(new Point3d(e, n, p.Ele ?? 0.0));
                }

                DrawPolyline(utm);

                ed.WriteMessage($"\nRuta importada: {TrackPoints.Count} puntos | Zona {SelectedUtmZone}");

                // === AQUÍ ESTÁ LA MAGIA: Abre el panel nativo y carga datos ===
                GpxPalette.Show();
                GpxPalette.SetTrack(TrackPoints);
            }
        }

        private static void DrawPolyline(List<Point3d> pts)
        {
            if (pts.Count == 0) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var poly = new Polyline();
                for (int i = 0; i < pts.Count; i++)
                    poly.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);

                poly.Color = Autodesk.AutoCAD.Colors.Color.FromColor(RouteColor);
                ms.AppendEntity(poly);
                tr.AddNewlyCreatedDBObject(poly, true);
                tr.Commit();
            }
        }

        public static void ShowRouteColorDialog()
        {
            using (var cd = new ColorDialog { Color = RouteColor })
            {
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    RouteColor = cd.Color;
                    RibbonUI.UpdateRouteSwatch(RouteColor);
                    UpdateRouteColorInDrawing();
                }
            }
        }

        private static void UpdateRouteColorInDrawing()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (ObjectId id in ms)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Polyline pl)
                    {
                        pl.UpgradeOpen();
                        pl.Color = Autodesk.AutoCAD.Colors.Color.FromColor(RouteColor);
                        break;
                    }
                }
                tr.Commit();
            }
        }

        // === CONTROLES DEL PANEL LATERAL ===
        public static void ToggleAutoSync() => GpxPalette.Control?.ToggleAutoSync();
    }
}
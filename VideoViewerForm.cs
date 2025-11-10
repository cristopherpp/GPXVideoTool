// GpxViewerForm.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System.Drawing;

using AutoCADApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinApp = System.Windows.Forms.Application;

namespace GPXVideoTools
{
    public class GpxViewerForm : Form
    {
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private VideoView _videoView;
        private DataGridView _grid;
        private Timer _syncTimer;
        private List<GpxPoint> _track;
        private ObjectId _markerId = ObjectId.Null;

        public GpxViewerForm()
        {
            Logger.Log("GpxViewerForm: Constructor iniciado");

            Core.Initialize();
            _libVLC = new LibVLC();
            Logger.Log("LibVLC inicializado");

            Text = "GPX Video Viewer (libVLC)";
            Width = 1200;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;

            // === LAYOUT PRINCIPAL: TableLayoutPanel ===
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F)); // Video: 50%
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Botones: 50px
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F)); // Grid: 50%
            Controls.Add(mainLayout);

            // === VIDEO ===
            _videoView = new VideoView { Dock = DockStyle.Fill };
            mainLayout.Controls.Add(_videoView, 0, 0);

            _mediaPlayer = new MediaPlayer(_libVLC);
            _videoView.MediaPlayer = _mediaPlayer;
            Logger.Log("MediaPlayer asignado al VideoView");

            // botones
            var pnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 50,
                FlowDirection = (System.Windows.Forms.FlowDirection)Autodesk.AutoCAD.DatabaseServices.FlowDirection.LeftToRight,
                Padding = new Padding(5)
            };

            var bplay = new Button { Text = "Play/Pause", Width = 90 }; bplay.Click += (s, e) => PlayPause();
            var bprev = new Button { Text = "< 5s", Width = 70 }; bprev.Click += (s, e) => SeekBackward();
            var bnext = new Button { Text = "> 5s", Width = 70 }; bnext.Click += (s, e) => SeekForward();
            var bauto = new Button { Text = "Sync ON/OFF", Width = 110 }; bauto.Click += (s, e) => ToggleAutoSync();

            // BOTONES DEBUG
            var bclear = new Button { Text = "Clear Log", Width = 90 }; bclear.Click += (s, e) => Logger.Clear();
            var bopen = new Button { Text = "Open Log", Width = 90 }; bopen.Click += (s, e) => Logger.OpenLogFile();
            var btest = new Button { Text = "TEST Marker", Width = 110 }; btest.Click += (s, e) => MoveMarkerTo(0);

            pnl.Controls.AddRange(new Control[] { bplay, bprev, bnext, bauto, bclear, bopen, btest });
            mainLayout.Controls.Add(pnl, 0, 1);

            // === GRID ===
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeRows = false
            };
            _grid.CellDoubleClick += (s, e) => SeekSelectedRow();
            mainLayout.Controls.Add(_grid, 0, 2);

            // === TIMER ===
            _syncTimer = new Timer { Interval = 500 };
            _syncTimer.Tick += SyncTimer_Tick;

            Logger.EnableFileLog = true;
            Logger.Log("GpxViewerForm: Constructor completado");
        }

        public void SetTrack(List<GpxPoint> t)
        {
            Logger.Log($"SetTrack llamado con {t?.Count ?? 0} puntos");
            _track = t;
            PopulateGrid();
        }

        private void PopulateGrid()
        {
            Logger.Log("PopulateGrid iniciado");

            var tbl = new System.Data.DataTable();
            tbl.Columns.Add("Idx", typeof(int));
            tbl.Columns.Add("Lat", typeof(double));
            tbl.Columns.Add("Lon", typeof(double));
            tbl.Columns.Add("Ele", typeof(double));
            tbl.Columns.Add("Time", typeof(string));
            tbl.Columns.Add("Seconds", typeof(double));

            if (_track == null || _track.Count == 0)
            {
                Logger.Log("Track vacío → grid vacío");
                _grid.DataSource = tbl;
                return;
            }

            DateTime baseT = _track[0].Time;
            Logger.Log($"BaseTime: {baseT:o}");

            for (int i = 0; i < _track.Count; i++)
            {
                var p = _track[i];
                double s = (p.Time - baseT).TotalSeconds;
                var r = tbl.NewRow();
                r["Idx"] = i;
                r["Lat"] = p.Lat;
                r["Lon"] = p.Lon;
                r["Ele"] = p.Ele ?? double.NaN;
                r["Time"] = p.Time.ToString("o");
                r["Seconds"] = s;
                tbl.Rows.Add(r);
            }

            _grid.DataSource = tbl;
            Logger.Log($"Grid poblado con {_track.Count} filas");
        }

        public void LoadVideo(string path)
        {
            Logger.Log($"LoadVideo: {path}");
            if (!File.Exists(path))
            {
                Logger.Error("Archivo de video no encontrado", new FileNotFoundException(path));
                return;
            }

            try
            {
                var media = new Media(_libVLC, new Uri(path));
                _mediaPlayer.Play(media);
                Logger.Log("Video iniciado");
            }
            catch (Exception ex)
            {
                Logger.Error("Error al reproducir video", ex);
            }
        }

        public void ApplyMarkerStyle(double size, Color color)
        {
            Logger.Log($"ApplyMarkerStyle: size={size}, color={color}");
            Commands.MarkerSize = size;
            Commands.MarkerColor = color;
            this.Invalidate();
        }

        private void SeekSelectedRow()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                Logger.Log("SeekSelectedRow: Ninguna fila seleccionada");
                return;
            }

            try
            {
                double secs = Convert.ToDouble(_grid.SelectedRows[0].Cells["Seconds"].Value);
                _mediaPlayer.Time = (long)(secs * 1000);
                _mediaPlayer.Play();
                Logger.Log($"Seek a {secs:F2}s");
            }
            catch (Exception ex)
            {
                Logger.Error("Error en SeekSelectedRow", ex);
            }
        }

        public void PlayPause()
        {
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                Logger.Log("Pausado");
            }
            else
            {
                _mediaPlayer.Play();
                Logger.Log("Reproduciendo");
            }
        }

        public void SeekBackward()
        {
            long t = Math.Max(0, _mediaPlayer.Time - 5000);
            _mediaPlayer.Time = t;
            Logger.Log($"Retrocedió 5s → {t / 1000.0}s");
        }

        public void SeekForward()
        {
            _mediaPlayer.Time += 5000;
            Logger.Log($"Avanzó 5s → {_mediaPlayer.Time / 1000.0}s");
        }

        public void ToggleAutoSync()
        {
            if (_syncTimer.Enabled)
            {
                _syncTimer.Stop();
                Logger.Log("AutoSync: DETENIDO");
            }
            else
            {
                _syncTimer.Start();
                Logger.Log("AutoSync: INICIADO");
            }
        }

        private void SyncTimer_Tick(object sender, EventArgs e)
        {
            if (_track == null || _track.Count == 0)
            {
                Logger.Log("SyncTimer_Tick: Track vacío");
                return;
            }

            double curr = _mediaPlayer.Time / 1000.0;
            DateTime baseT = _track[0].Time;
            int idx = 0;
            double md = double.MaxValue;

            for (int i = 0; i < _track.Count; i++)
            {
                double s = (_track[i].Time - baseT).TotalSeconds;
                double d = Math.Abs(s - curr);
                if (d < md) { md = d; idx = i; }
            }

            Logger.Log($"Sync: Video={curr:F2}s → GPX={(_track[idx].Time - baseT).TotalSeconds:F2}s (índice {idx})");

            if (_grid.Rows.Count > idx)
            {
                _grid.ClearSelection();
                _grid.Rows[idx].Selected = true;
                _grid.FirstDisplayedScrollingRowIndex = idx;
            }

            MoveMarkerTo(idx);
            CenterViewOn(idx);
        }

        private void CenterViewOn(int idx)
        {
            if (_track == null || idx >= _track.Count) return;

            var p = _track[idx];
            int zone; bool north;
            Utils.ParseZoneString(Commands.SelectedUtmZone, out zone, out north);
            double e, n; int zu; bool nh;
            Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out e, out n, out zu, out nh);

            var doc = AutoCADApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Logger.Log("CenterViewOn: doc es NULL");
                return;
            }

            // ESPERAR A QUE EL EDITOR ESTÉ LISTO
            if (doc.Editor.IsQuiescent == false)
            {
                Logger.Log("Editor no está listo (IsQuiescent = false). Reintentando...");
                // Reintentar en 100ms
                var retryTimer = new System.Windows.Forms.Timer { Interval = 100 };
                retryTimer.Tick += (s, ev) =>
                {
                    retryTimer.Stop();
                    retryTimer.Dispose();
                    CenterViewOn(idx); // Reintento
                };
                retryTimer.Start();
                return;
            }

            using (doc.LockDocument())
            {
                try
                {
                    doc.Editor.CommandAsync($"_.ZOOM _C {e} {n} 0.0 50.0\n");
                    Logger.Log($"Zoom centrado en UTM({e:F1}, {n:F1})");
                }
                catch (Exception ex)
                {
                    Logger.Error("Zoom falló", ex);
                }
            }
        }

        private void MoveMarkerTo(int idx)
        {
            Logger.Log($"MoveMarkerTo({idx}) iniciado");

            if (_track == null || idx < 0 || idx >= _track.Count)
            {
                Logger.Log("Índice inválido");
                return;
            }

            var p = _track[idx];
            int zone; bool north;
            Utils.ParseZoneString(Commands.SelectedUtmZone, out zone, out north);
            double e, n; int zu; bool nh;
            Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out e, out n, out zu, out nh);
            double z = p.Ele ?? 0.0;

            double ang = 0.0;
            if (idx < _track.Count - 1)
            {
                double e2, n2;
                Utils.LatLonToUtm(_track[idx + 1].Lat, _track[idx + 1].Lon, zone, north, out e2, out n2, out zu, out nh);
                ang = Math.Atan2(n2 - n, e2 - e);
            }
            else if (idx > 0)
            {
                double e2, n2;
                Utils.LatLonToUtm(_track[idx - 1].Lat, _track[idx - 1].Lon, zone, north, out e2, out n2, out zu, out nh);
                ang = Math.Atan2(n - n2, e - e2);
            }

            Logger.Log($"Marker: UTM({e:F1}, {n:F1}), Z={z:F1}, Ang={ang * 180 / Math.PI:F1}°");

            var doc = AutoCADApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Logger.Log("doc es NULL → no hay documento activo");
                return;
            }

            using (doc.LockDocument())
            {
                var db = doc.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        ObjectId markerBlockId;
                        if (!bt.Has("GPX_MARKER"))
                        {
                            Logger.Log("Creando bloque GPX_MARKER...");

                            var btr = new BlockTableRecord { Name = "GPX_MARKER" };
                            var shaft = new Polyline();
                            shaft.AddVertexAt(0, new Point2d(-0.5, 0), 0, 0, 0);
                            shaft.AddVertexAt(1, new Point2d(0.4, 0), 0, 0, 0);
                            shaft.Color = Autodesk.AutoCAD.Colors.Color.FromColor(Commands.MarkerColor);

                            var head = new Polyline();
                            head.AddVertexAt(0, new Point2d(0.4, 0.18), 0, 0, 0);
                            head.AddVertexAt(1, new Point2d(1.0, 0), 0, 0, 0);
                            head.AddVertexAt(2, new Point2d(0.4, -0.18), 0, 0, 0);
                            head.Closed = true;
                            head.Color = Autodesk.AutoCAD.Colors.Color.FromColor(Commands.MarkerColor);

                            btr.AppendEntity(shaft);
                            btr.AppendEntity(head);

                            bt.UpgradeOpen();
                            markerBlockId = bt.Add(btr);
                            tr.AddNewlyCreatedDBObject(btr, true);
                            Logger.Log("Bloque GPX_MARKER creado");
                        }
                        else
                        {
                            markerBlockId = bt["GPX_MARKER"];
                            Logger.Log("Bloque GPX_MARKER ya existe");
                        }

                        BlockReference existingRef = null;
                        foreach (ObjectId id in ms)
                        {
                            var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent is BlockReference bref)
                            {
                                var btr = tr.GetObject(bref.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                                if (btr?.Name.Equals("GPX_MARKER", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    existingRef = bref;
                                    break;
                                }
                            }
                        }

                        if (existingRef == null || existingRef.IsErased)
                        {
                            Logger.Log("Creando nueva referencia de marcador");
                            var newBr = new BlockReference(new Point3d(e, n, z), markerBlockId);
                            newBr.ScaleFactors = new Scale3d(Commands.MarkerSize);
                            ms.AppendEntity(newBr);
                            tr.AddNewlyCreatedDBObject(newBr, true);
                            _markerId = newBr.ObjectId;
                        }
                        else
                        {
                            Logger.Log("Actualizando marcador existente");
                            var brToModify = (BlockReference)tr.GetObject(existingRef.ObjectId, OpenMode.ForWrite);
                            brToModify.Position = new Point3d(e, n, z);
                            brToModify.Rotation = ang;
                            brToModify.ScaleFactors = new Scale3d(Commands.MarkerSize);
                            brToModify.Color = Autodesk.AutoCAD.Colors.Color.FromColor(Commands.MarkerColor);
                        }

                        tr.Commit();
                        Logger.Log("MoveMarkerTo: ÉXITO");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("FALLÓ MoveMarkerTo", ex);
                    }
                }
            }
        }
    }
}
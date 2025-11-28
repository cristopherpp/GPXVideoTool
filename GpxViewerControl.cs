using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GPXVideoTools
{
    public partial class GpxViewerControl : UserControl
    {
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private VideoView _videoView;
        private DataGridView _grid;
        private Timer _syncTimer;
        private List<GpxPoint> _track;
        private ObjectId _markerId = ObjectId.Null;
        private Point2d _lastViewCenter = Point2d.Origin;
        private const double MIN_MOVE_DISTANCE = 25.0;
        private const int SYNC_INTERVAL_MS = 100;
        private bool _isSyncActive = false;

        public static string LastVideoPath { get; set; }

        public GpxViewerControl()
        {
            InitializeComponent();
            SetupUI();
            InitializeVLC();
        }

        private void InitializeVLC()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            _videoView.MediaPlayer = _mediaPlayer;
        }

        private void SetupUI()
        {
            this.Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(5)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            this.Controls.Add(layout);

            // Video
            _videoView = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black };
            layout.Controls.Add(_videoView, 0, 0);

            // Botones
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(2)
            };

            var buttons = new[]
            {
                ("Play/Pause", (Action)PlayPause),
                ("< 5s", SeekBackward),
                ("> 5s", SeekForward),
                ("Sync ON/OFF", ToggleAutoSync),
                ("Import Video", ImportVideoFromDialog),
                ("Clear Log", () => Logger.Clear()),
                ("Open Log", () => Logger.OpenLogFile()),
                ("TEST Marker", () => MoveMarkerTo(0))
            };

            foreach (var (text, action) in buttons)
            {
                var btn = new Button { Text = text, Width = 100, Height = 30, Margin = new Padding(3) };
                btn.Click += (s, e) => action();
                btnPanel.Controls.Add(btn);
            }
            layout.Controls.Add(btnPanel, 0, 1);

            // Grid
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeRows = false
            };
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) SeekSelectedRow(); };
            layout.Controls.Add(_grid, 0, 2);

            // Timer
            _syncTimer = new Timer { Interval = SYNC_INTERVAL_MS };
            _syncTimer.Tick += SyncTimer_Tick;

            Logger.EnableFileLog = true;
            Logger.Log("GpxViewerControl creado correctamente");
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

        private static string GetFileSize(string filePath)
        {
            try
            {
                var info = new System.IO.FileInfo(filePath);
                long bytes = info.Length;
                string[] suffixes = { "B", "KB", "MB", "GB" };
                int index = 0;
                double size = bytes;
                while (size >= 1024 && index < suffixes.Length - 1)
                {
                    size /= 1024;
                    index++;
                }
                return $"{size:0.##} {suffixes[index]}";
            }
            catch
            {
                return "Desconocido";
            }
        }

        private void ImportVideoFromDialog()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("No hay documento activo en AutoCAD.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var ed = doc.Editor;

            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Seleccionar video";
                ofd.Filter = "Videos MP4 y AVI|*.mp4;*.avi|MP4 (*.mp4)|*.mp4|AVI (*.avi)|*.avi";
                ofd.FilterIndex = 1;

                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    ed.WriteMessage("\nImportación de video cancelada.");
                    return;
                }

                string path = ofd.FileName;
                string ext = Path.GetExtension(path).ToLowerInvariant();

                if (ext != ".mp4" && ext != ".avi")
                {
                    ed.WriteMessage($"\nError: Solo se permiten .mp4 y .avi");
                    return;
                }

                LastVideoPath = path;
                ed.WriteMessage($"\nVideo cargado: {Path.GetFileName(path)} [{GetFileSize(path)}]");
                LoadVideo(path);
            }
        }

        public void LoadVideo(string path)
        {
            Logger.Log($"Cargando video: {path}");
            if (!File.Exists(path))
            {
                Logger.Error("Video no encontrado", new FileNotFoundException(path));
                MessageBox.Show("Video no encontrado:\n" + path, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                if (_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Stop();
                }

                using (var media = new Media(_libVLC, new Uri(path)))
                {
                    _mediaPlayer.Media = media;
                }

                _mediaPlayer.Play();

                _mediaPlayer.SetPause(true);
                Logger.Log("Video reproducido correctamente");
            }
            catch (System.Exception ex)
            {
                Logger.Error("Error al reproducir video", ex);
                MessageBox.Show($"Error VLC:\n{ex.Message}", "Error de reproducción", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
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
                _isSyncActive = false;
                Logger.Log("AutoSync: OFF");
            }
            else
            {
                if (_mediaPlayer.IsPlaying || (_track != null && _track.Count > 0))
                {
                    _syncTimer.Start();
                    _isSyncActive = true;
                    Logger.Log("AutoSync: ON");
                }
                else
                {
                    MessageBox.Show("Reproduce un video o carga un GPX para sincronizar.", "Sync", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void SyncTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying || !_isSyncActive)
                return;

            double videoTime = _mediaPlayer.Time / 1000.0;

            if (_track == null || _track.Count == 0)
            {
                Logger.Log($"Sync: Video={videoTime:F2}s (sin GPX)");
                return;
            }

            DateTime baseT = _track[0].Time;
            int bestIdx = 0;
            double minDiff = double.MaxValue;
            for (int i = 0; i < _track.Count; i++)
            {
                double gpxTime = (_track[i].Time - baseT).TotalSeconds;
                double diff = Math.Abs(gpxTime - videoTime);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestIdx = i;
                }
            }

            if (_grid.Rows.Count > bestIdx)
            {
                _grid.ClearSelection();
                _grid.Rows[bestIdx].Selected = true;
                _grid.FirstDisplayedScrollingRowIndex = bestIdx;
            }

            MoveMarkerTo(bestIdx);
            // CenterViewOn(bestIdx);
            Logger.Log($"Sync OK: Video={videoTime:F2}s → GPX={bestIdx} ({minDiff:F3}s diff)");
        }

        // CORRECCIÓN: Seguimiento suave SIN regeneración, SIN zoom fijo, SIN comandos
        /* private void CenterViewOn(int idx)
        {
            if (!_isSyncActive || _track == null || idx >= _track.Count) return;

            var p = _track[idx];
            int zone; bool north;
            Utils.ParseZoneString(Commands.SelectedUtmZone, out zone, out north);

            Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out double e, out double n, out _, out _);
            //Point2d target = new Point2d(e, n);

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ed = doc.Editor;
                var view = ed.GetCurrentView();

                // Evitar movimientos innecesarios
                if (_lastViewCenter != Point2d.Origin)
                {
                    double dist = target.GetDistanceTo(_lastViewCenter);
                    if (dist < MIN_MOVE_DISTANCE)
                    {
                        tr.Commit();
                        return;
                    }
                }

                // Mantener zoom actual
                double currentWidth = view.Width;
                if (currentWidth < 10) currentWidth = 100;

                view.CenterPoint = target;
                view.Width = currentWidth;
                view.Height = currentWidth * (view.Height / view.Width);

                ed.SetCurrentView(view);
                _lastViewCenter = target;

                Logger.Log($"Vista centrada en: {e:F1}, {n:F1}");
                tr.Commit();
            }
        } */

        private void MoveMarkerTo(int idx)
        {
            Logger.Log($"MoveMarkerTo({idx}) iniciado");
            if (!_isSyncActive || _track == null || idx < 0 || idx >= _track.Count)
            {
                Logger.Log("Índice inválido o sync inactivo");
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

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        Logger.Error("FALLÓ MoveMarkerTo", ex);
                    }
                }
            }
        }
    }
}
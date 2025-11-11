// GpxViewerForm.cs
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
        public static string LastVideoPath { get; set; }


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
            var bimport = new Button { Text = "Import", Width = 100 }; bimport.Click += (s, e) => ImportVideoFromDialog();

            // BOTONES DEBUG
            var bclear = new Button { Text = "Clear Log", Width = 90 }; bclear.Click += (s, e) => Logger.Clear();
            var bopen = new Button { Text = "Open Log", Width = 90 }; bopen.Click += (s, e) => Logger.OpenLogFile();
            var btest = new Button { Text = "TEST Marker", Width = 110 }; btest.Click += (s, e) => MoveMarkerTo(0);

            pnl.Controls.AddRange(new Control[] { bplay, bprev, bnext, bauto, bimport, bclear, bopen, btest });
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

        // Función axiliar de videoImport
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
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Seleccionar video (.mp4 o .avi)";
                ofd.Filter = "Videos (*.mp4;*.avi)|*.mp4;*.avi|MP4 (*.mp4)|*.mp4|AVI (*.avi)|*.avi";
                ofd.FilterIndex = 1;

                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    ed.WriteMessage("\nCancelado.");
                    return;
                }

                string path = ofd.FileName;
                string ext = Path.GetExtension(path).ToLower();

                if (ext != ".mp4" && ext != ".avi")
                {
                    ed.WriteMessage($"\nError: Solo .mp4 y .avi permitidos. ({ext})");
                    return;
                }

                LastVideoPath = path;
                ed.WriteMessage($"\nVideo cargado: {Path.GetFileName(path)}");
                ed.WriteMessage($"\nTamaño: {GetFileSize(path)}");

                LoadVideo(path);

                this.Show();
                this.BringToFront();
                this.WindowState = FormWindowState.Normal;
            }
        }

        // LoadVideo para cargar el video
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
                _mediaPlayer.Stop();

                var media = new Media(_libVLC, new Uri(path));
                _mediaPlayer.Play(media);
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
                Logger.Log("AutoSync: OFF");
            }
            else
            {
                if (_mediaPlayer.IsPlaying || (_track != null && _track.Count > 0))
                {
                    _syncTimer.Start();
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
            // Si no hay video reproduciéndose → salir
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying)
                return;

            double videoTime = _mediaPlayer.Time / 1000.0;

            // Si NO hay track → solo actualizar UI básica
            if (_track == null || _track.Count == 0)
            {
                Logger.Log($"Sync: Video={videoTime:F2}s (sin GPX)");
                return;
            }

            // Hay track → sincronizar
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

            // Actualizar grid
            if (_grid.Rows.Count > bestIdx)
            {
                _grid.ClearSelection();
                _grid.Rows[bestIdx].Selected = true;
                _grid.FirstDisplayedScrollingRowIndex = bestIdx;
            }

            MoveMarkerTo(bestIdx);
            CenterViewOn(bestIdx);

            Logger.Log($"Sync OK: Video={videoTime:F2}s → GPX={bestIdx} ({minDiff:F3}s diff)");
        }

        private void CenterViewOn(int idx)
        {
            if (_track == null || idx >= _track.Count) return;
            var p = _track[idx];

            int zone; bool north;
            Utils.ParseZoneString(Commands.SelectedUtmZone, out zone, out north);
            Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out double e, out double n, out _, out _);

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Método seguro y síncrono
            doc.SendStringToExecute($"_.ZOOM _C {e:F2} {n:F2} 50 ", true, false, true);
            Logger.Log($"Zoom centrado en: {e:F1}, {n:F1}");
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
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        Logger.Error("FALLÓ MoveMarkerTo", ex);
                    }
                }
            }
        }
    }
}
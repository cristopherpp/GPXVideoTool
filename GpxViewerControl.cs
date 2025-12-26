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
using System.Threading.Tasks;
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

        // --- UI COMPONENTS ---
        private Panel _videoContainer;
        private Panel _telemetryBar;
        private Label _lblRouteInfo;
        private Label _lblDistInfo;
        private Button _btnPlay;

        // --- LOGIC VARIABLES ---
        private ObjectId _markerId = ObjectId.Null;
        private const int SYNC_INTERVAL_MS = 100;
        private bool _isSyncActive = false;
        private Point3d _lastPos = new Point3d(0, 0, 0);

        public static string LastVideoPath { get; set; }

        public GpxViewerControl()
        {
            InitializeComponent();
            this.BackColor = Color.FromArgb(40, 40, 40);
            SetupUI();
            InitializeVLC();
        }

        private void InitializeVLC()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            _videoView.MediaPlayer = _mediaPlayer;
            _mediaPlayer.Volume = 50; // Set default volume since scroll works now

            // NATIVE MODE: We do NOT disable mouse input. We let VLC handle everything.

            // Smart Button Events
            _mediaPlayer.EndReached += (s, e) => this.BeginInvoke(new Action(() => _btnPlay.Text = "Replay"));
            _mediaPlayer.Playing += (s, e) => this.BeginInvoke(new Action(() => _btnPlay.Text = "Pause"));
            _mediaPlayer.Paused += (s, e) => this.BeginInvoke(new Action(() => _btnPlay.Text = "Play"));
            _mediaPlayer.Stopped += (s, e) => this.BeginInvoke(new Action(() => _btnPlay.Text = "Play"));
        }

        private void SetupUI()
        {
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(0);
            this.Margin = new Padding(0);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0),
                BackColor = Color.FromArgb(40, 40, 40)
            };

            // STRICT LAYOUT: 80% Video, 20% Grid
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 80F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));

            this.Controls.Add(mainLayout);

            // 1. VIDEO CONTAINER
            _videoContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            mainLayout.Controls.Add(_videoContainer, 0, 0);

            // 1.1 VIDEO VIEW
            _videoView = new VideoView
            {
                // NATIVE MODE: Dock Fill lets VLC automatically scale the video to the window
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Margin = new Padding(0)
            };
            _videoContainer.Controls.Add(_videoView);

            // 2. TELEMETRY
            _telemetryBar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(55, 55, 55),
                Padding = new Padding(5, 0, 5, 0),
                Margin = new Padding(0)
            };
            mainLayout.Controls.Add(_telemetryBar, 0, 1);

            _lblRouteInfo = new Label { Text = "NO FILE", ForeColor = Color.LightGray, Dock = DockStyle.Left, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9F) };
            _lblDistInfo = new Label { Text = "0.00 km", ForeColor = Color.Cyan, Dock = DockStyle.Right, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9.5F, FontStyle.Bold) };
            _telemetryBar.Controls.Add(_lblRouteInfo);
            _telemetryBar.Controls.Add(_lblDistInfo);

            // 3. CONTROLS
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(2),
                Margin = new Padding(0),
                BackColor = Color.FromArgb(40, 40, 40)
            };

            CreateButton(btnPanel, "Load GPX", 75, () => Commands.ImportAndOpen());
            CreateButton(btnPanel, "Load Video", 80, ImportVideoFromDialog);

            _btnPlay = CreateButton(btnPanel, "Play", 55, PlayPause);

            CreateButton(btnPanel, "< 5s", 45, SeekBackward);
            CreateButton(btnPanel, "> 5s", 45, SeekForward);
            CreateButton(btnPanel, "Sync", 50, ToggleAutoSync);

            // Removed Z+, Z-, Reset buttons as requested (Native Mode)

            mainLayout.Controls.Add(btnPanel, 0, 2);

            // 4. GRID
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeRows = false,
                BackgroundColor = Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(65, 65, 65)
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 35);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(50, 50, 50);
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(80, 80, 80);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;

            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) SeekSelectedRow(); };
            mainLayout.Controls.Add(_grid, 0, 3);

            _syncTimer = new Timer { Interval = SYNC_INTERVAL_MS };
            _syncTimer.Tick += SyncTimer_Tick;
        }

        private Button CreateButton(Panel parent, string text, int width, Action action)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(90, 90, 90);
            btn.Click += (s, e) => action();
            parent.Controls.Add(btn);
            return btn;
        }

        // =========================================================
        // DATA & SEEKING LOGIC
        // =========================================================

        public void SetTrack(List<GpxPoint> t)
        {
            _track = t;
            PopulateGrid();
            _lblRouteInfo.Text = $"GPX: {_track.Count} pts";
            if (!_syncTimer.Enabled) ToggleAutoSync();
        }

        private void PopulateGrid()
        {
            var tbl = new System.Data.DataTable();
            tbl.Columns.Add("Idx", typeof(int));
            tbl.Columns.Add("Dist (km)", typeof(double));
            tbl.Columns.Add("Lat", typeof(double));
            tbl.Columns.Add("Lon", typeof(double));
            tbl.Columns.Add("Ele", typeof(double));
            tbl.Columns.Add("Time", typeof(string));
            tbl.Columns.Add("Seconds", typeof(double));

            if (_track == null || _track.Count == 0) { _grid.DataSource = tbl; return; }

            DateTime baseT = _track[0].Time;
            double runningDist = 0;

            for (int i = 0; i < _track.Count; i++)
            {
                var p = _track[i];
                if (i > 0)
                {
                    runningDist += Utils.CalculateDistance(
                        _track[i - 1].Lat, _track[i - 1].Lon,
                        p.Lat, p.Lon);
                }

                var r = tbl.NewRow();
                r["Idx"] = i;
                r["Dist (km)"] = Math.Round(runningDist, 3);
                r["Lat"] = p.Lat;
                r["Lon"] = p.Lon;
                r["Ele"] = p.Ele ?? 0;
                r["Time"] = p.Time.ToString("HH:mm:ss");
                r["Seconds"] = Math.Round((p.Time - baseT).TotalSeconds, 1);
                tbl.Rows.Add(r);
            }
            _grid.DataSource = tbl;
        }

        private void SyncTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying || !_isSyncActive || _track == null || _track.Count == 0) return;

            double videoTime = _mediaPlayer.Time / 1000.0;
            DateTime baseT = _track[0].Time;
            int bestIdx = 0;
            double minDiff = double.MaxValue;
            for (int i = 0; i < _track.Count; i++)
            {
                double diff = Math.Abs((_track[i].Time - baseT).TotalSeconds - videoTime);
                if (diff < minDiff) { minDiff = diff; bestIdx = i; }
            }

            if (_grid.Rows.Count > bestIdx && _grid.FirstDisplayedScrollingRowIndex != bestIdx)
            {
                _grid.ClearSelection();
                _grid.Rows[bestIdx].Selected = true;
                _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, bestIdx - 2);
            }

            double totalDist = 0;
            if (_grid.Rows.Count > bestIdx)
            {
                try { totalDist = Convert.ToDouble(_grid.Rows[bestIdx].Cells["Dist (km)"].Value); } catch { }
            }

            string fName = string.IsNullOrEmpty(LastVideoPath) ? "No Video" : Path.GetFileName(LastVideoPath);
            _lblRouteInfo.Text = $"File: {fName}";
            _lblDistInfo.Text = $"{totalDist:F3} km";

            MoveMarkerTo(bestIdx);
        }

        public void PlayPause()
        {
            if (_mediaPlayer.State == VLCState.Ended)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Play();
            }
            else if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                _mediaPlayer.Play();
            }
        }

        private void SeekSelectedRow()
        {
            if (_grid.SelectedRows.Count == 0) return;
            try
            {
                double s = Convert.ToDouble(_grid.SelectedRows[0].Cells["Seconds"].Value);
                long seekTime = (long)(s * 1000);

                if (_mediaPlayer.State == VLCState.Ended || _mediaPlayer.State == VLCState.Stopped)
                {
                    _mediaPlayer.Play();
                    System.Threading.Thread.Sleep(100);
                }

                _mediaPlayer.Time = seekTime;
                if (!_mediaPlayer.IsPlaying) _mediaPlayer.Play();
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex.Message);
            }
        }

        private void MoveMarkerTo(int idx)
        {
            if (!_isSyncActive || _track == null || idx < 0 || idx >= _track.Count) return;
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var p = _track[idx];
            int zone; bool north;

            if (string.IsNullOrEmpty(Commands.SelectedUtmZone)) { zone = (int)((p.Lon + 180) / 6) + 1; north = p.Lat >= 0; }
            else { Utils.ParseZoneString(Commands.SelectedUtmZone, out zone, out north); }

            Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out double e, out double n, out _, out _);
            Point3d newPos = new Point3d(e, n, p.Ele ?? 0);

            if (_lastPos.DistanceTo(newPos) < 0.1) return;
            _lastPos = newPos;

            double ang = 0;
            if (idx < _track.Count - 1)
            {
                Utils.LatLonToUtm(_track[idx + 1].Lat, _track[idx + 1].Lon, zone, north, out double e2, out double n2, out _, out _);
                ang = Math.Atan2(n2 - n, e2 - e);
            }

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (_markerId.IsValid && !_markerId.IsErased)
                    {
                        var blkRef = (BlockReference)tr.GetObject(_markerId, OpenMode.ForWrite);
                        blkRef.Position = newPos;
                        blkRef.Rotation = ang;
                    }
                    else
                    {
                        var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        ObjectId markerDefId = bt.Has("GPX_MARKER") ? bt["GPX_MARKER"] : CreateMarker(tr, bt);
                        var newRef = new BlockReference(newPos, markerDefId) { ScaleFactors = new Scale3d(Commands.MarkerSize), Rotation = ang };
                        ms.AppendEntity(newRef);
                        tr.AddNewlyCreatedDBObject(newRef, true);
                        _markerId = newRef.ObjectId;
                    }
                    tr.Commit();
                    doc.TransactionManager.QueueForGraphicsFlush();
                }
                doc.Editor.UpdateScreen();
                System.Windows.Forms.Application.DoEvents();
            }
            catch (System.Exception) { }
        }

        private ObjectId CreateMarker(Transaction tr, BlockTable bt)
        {
            var btr = new BlockTableRecord { Name = "GPX_MARKER" };
            var head = new Polyline();
            head.AddVertexAt(0, new Point2d(-0.6, 0.20), 0, 0, 0);
            head.AddVertexAt(1, new Point2d(0.0, 0.0), 0, 0, 0);
            head.AddVertexAt(2, new Point2d(-0.6, -0.20), 0, 0, 0);
            head.Closed = true;
            head.Color = Autodesk.AutoCAD.Colors.Color.FromColor(Commands.MarkerColor);
            btr.AppendEntity(head);
            bt.UpgradeOpen();
            ObjectId id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            return id;
        }

        private void ImportVideoFromDialog()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (var ofd = new OpenFileDialog { Filter = "Videos|*.mp4;*.avi" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    LastVideoPath = ofd.FileName;
                    LoadVideo(LastVideoPath);
                }
            }
        }

        public void LoadVideo(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                if (_mediaPlayer.IsPlaying) _mediaPlayer.Stop();
                using (var media = new Media(_libVLC, new Uri(path))) _mediaPlayer.Media = media;
                _mediaPlayer.Play();
                System.Threading.Thread.Sleep(150);
                // We do NOT set pause here because VLC needs to keep playing for the user to see it
                // _mediaPlayer.SetPause(true); 
                _lblRouteInfo.Text = "Ready: " + Path.GetFileName(path);
            }
            catch (System.Exception) { }
        }

        public void SeekBackward() => _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 5000);
        public void SeekForward() => _mediaPlayer.Time += 5000;
        public void ToggleAutoSync() { if (_syncTimer.Enabled) { _syncTimer.Stop(); _isSyncActive = false; } else { _syncTimer.Start(); _isSyncActive = true; } }
    }
}
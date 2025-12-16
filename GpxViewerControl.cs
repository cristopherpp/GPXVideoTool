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

        // PERFORMANCE: We cache the ID so we don't search for it 10 times/second
        private ObjectId _markerId = ObjectId.Null;

        private const int SYNC_INTERVAL_MS = 100;
        private bool _isSyncActive = false;
        private float _currentZoom = 1.0f;

        // New Video Properties (EXPERIMENTAL)
        private Panel _videoContainer;
        private Point _dragStartPoint;
        private bool _isDragging = false;

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

            // --- RESPONSIVE LAYOUT ---
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70F)); // Video
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Buttons (Smart)
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F)); // Grid
            this.Controls.Add(layout);

            // 0. Container
            _videoContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                AutoScroll = false // We handle movement manually for smoothness
            };
            layout.Controls.Add(_videoContainer, 0, 0);

            // 1. Video
            _videoView = new VideoView
            {
                // Important: DO NOT use DockStyle.Fill here. We need to resize it manually.
                Anchor = AnchorStyles.None,
                Location = new Point(0, 0),
                BackColor = Color.Black,
                Size = new Size(800, 600) // Initial size, will be fixed by Resize event
            };
            _videoContainer.Controls.Add(_videoView);

            // 2. Events for Panning (Dragging)
            _videoView.MouseDown += VideoView_MouseDown;
            _videoView.MouseMove += VideoView_MouseMove;
            _videoView.MouseUp += VideoView_MouseUp;

            // 3. Event for Zooming (Mouse Wheel)
            _videoView.MouseWheel += VideoView_MouseWheel;

            // Zoom with Mouse Wheel
            _videoView.MouseWheel += (s, e) => ZoomVideo(e.Delta > 0 ? 1.1f : 0.9f);

            _videoContainer.Resize += (s, e) =>
            {
                if (_currentZoom == 1.0f)
                {
                    _videoView.Size = _videoContainer.Size;
                    _videoView.Location = new Point(0, 0);
                }
            };

            // 4. Buttons
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            // Fix: Use explicit type to avoid compiler errors
            var buttons = new (string, Action)[]
            {
                ("Import GPX", () => Commands.ImportAndOpen()), // Points to static command
                ("Import Video", ImportVideoFromDialog),
                ("Play/Pause", PlayPause),
                ("< 5s", SeekBackward),
                ("> 5s", SeekForward),
                ("Sync ON/OFF", ToggleAutoSync),
                ("Video +", () => ZoomVideo(1.2f)),
                ("Video -", () => ZoomVideo(0.8f)),
                ("Reset", () => ZoomVideo(0.0f))
            };

            foreach (var (text, action) in buttons)
            {
                var btn = new Button
                {
                    Text = text,
                    Width = 90,
                    Height = 30,
                    Margin = new Padding(2),
                    Cursor = Cursors.Hand
                };
                btn.Click += (s, e) => action();
                btnPanel.Controls.Add(btn);
            }
            layout.Controls.Add(btnPanel, 0, 1);

            // 5. Grid
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeRows = false,
                BackgroundColor = SystemColors.ControlLight,
                BorderStyle = BorderStyle.None
            };
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) SeekSelectedRow(); };
            layout.Controls.Add(_grid, 0, 2);

            _syncTimer = new Timer { Interval = SYNC_INTERVAL_MS };
            _syncTimer.Tick += SyncTimer_Tick;
        }

        public void SetTrack(List<GpxPoint> t)
        {
            _track = t;
            PopulateGrid();
        }

        private void PopulateGrid()
        {
            var tbl = new System.Data.DataTable();
            tbl.Columns.Add("Idx", typeof(int));
            tbl.Columns.Add("Lat", typeof(double));
            tbl.Columns.Add("Lon", typeof(double));
            tbl.Columns.Add("Ele", typeof(double));
            tbl.Columns.Add("Time", typeof(string));
            tbl.Columns.Add("Seconds", typeof(double));
            if (_track == null || _track.Count == 0)
            {
                _grid.DataSource = tbl;
                return;
            }
            DateTime baseT = _track[0].Time;
            for (int i = 0; i < _track.Count; i++)
            {
                var p = _track[i];
                double s = (p.Time - baseT).TotalSeconds;
                var r = tbl.NewRow();
                r["Idx"] = i;
                r["Lat"] = p.Lat;
                r["Lon"] = p.Lon;
                r["Ele"] = p.Ele ?? double.NaN;
                r["Time"] = p.Time.ToString("HH:mm:ss"); // Cleaner time format
                r["Seconds"] = Math.Round(s, 1);
                tbl.Rows.Add(r);
            }
            _grid.DataSource = tbl;
        }

        private void ImportVideoFromDialog()
        {
            // (Same as your code)
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Videos|*.mp4;*.avi";
                if (ofd.ShowDialog() != DialogResult.OK) return;
                LastVideoPath = ofd.FileName;
                LoadVideo(LastVideoPath);
            }
        }

        public void LoadVideo(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                if (_mediaPlayer.IsPlaying) _mediaPlayer.Stop();
                using (var media = new Media(_libVLC, new Uri(path)))
                {
                    _mediaPlayer.Media = media;
                }
                _mediaPlayer.Play();
                // Brief pause so it loads but doesn't auto-run
                System.Threading.Thread.Sleep(100);
                _mediaPlayer.SetPause(true);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error loading video: {ex.Message}");
            }
        }

        private void VideoView_MouseWheel(object sender, MouseEventArgs e)
        {
            // 1. Calculate Zoom Factor
            float oldZoom = _currentZoom;
            float zoomAmount = e.Delta > 0 ? 1.2f : 0.8f; // 20% zoom steps
            float newZoom = _currentZoom * zoomAmount;

            // Safety Limits (1x to 5x)
            if (newZoom < 1.0f) newZoom = 1.0f;
            if (newZoom > 8.0f) newZoom = 8.0f;

            if (newZoom == oldZoom) return; // No change
            _currentZoom = newZoom;

            // 2. Get Mouse Position Relative to the Video (Before Zoom)
            // This is the specific point user is looking at (e.g., the pothole)
            Point mousePos = e.Location;

            // 3. Resize the VideoView
            int newWidth = (int)(_videoContainer.Width * _currentZoom);
            int newHeight = (int)(_videoContainer.Height * _currentZoom);

            // If resetting to 1.0, just fit perfectly
            if (_currentZoom == 1.0f)
            {
                _videoView.Size = _videoContainer.Size;
                _videoView.Location = new Point(0, 0);
                return;
            }

            // 4. Calculate New Location to Keep Mouse Centered
            // The math: OldMousePos * ScaleRatio - MouseOffset
            float ratio = newZoom / oldZoom;

            int newX = (int)(mousePos.X - (mousePos.X - _videoView.Left) * ratio);
            int newY = (int)(mousePos.Y - (mousePos.Y - _videoView.Top) * ratio);

            _videoView.Size = new Size(newWidth, newHeight);

            // Apply position with boundary checks (Don't let video fly off screen)
            SetVideoLocation(newX, newY);
        }

        private void VideoView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _currentZoom > 1.0f)
            {
                _isDragging = true;
                _dragStartPoint = e.Location;
                _videoView.Cursor = Cursors.SizeAll; // Hand icon
            }
        }

        private void VideoView_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                // Calculate how much moved
                int deltaX = e.X - _dragStartPoint.X;
                int deltaY = e.Y - _dragStartPoint.Y;

                // Apply to current location
                SetVideoLocation(_videoView.Left + deltaX, _videoView.Top + deltaY);
            }
        }

        private void VideoView_MouseUp(object sender, MouseEventArgs e)
        {
            _isDragging = false;
            _videoView.Cursor = Cursors.Default;
        }

        private void SetVideoLocation(int x, int y)
        {
            // Boundary Checks: We never want a gap between video edge and container edge
            // Max X is 0 (Aligned left), Min X is ContainerWidth - VideoWidth

            if (x > 0) x = 0;
            if (y > 0) y = 0;

            if (x < _videoContainer.Width - _videoView.Width)
                x = _videoContainer.Width - _videoView.Width;

            if (y < _videoContainer.Height - _videoView.Height)
                y = _videoContainer.Height - _videoView.Height;

            _videoView.Location = new Point(x, y);
        }

        private void ZoomVideo(float factor)
        {
            // 1. Handle Reset (Factor 0)
            if (factor == 0.0f)
            {
                _currentZoom = 1.0f;
                _videoView.Size = _videoContainer.Size;
                _videoView.Location = new Point(0, 0);
                return;
            }

            // 2. Calculate New Zoom
            float oldZoom = _currentZoom;
            float newZoom = _currentZoom * factor; // e.g. 1.0 * 1.1 = 1.1

            // Safety Limits
            if (newZoom < 1.0f) newZoom = 1.0f;
            if (newZoom > 8.0f) newZoom = 8.0f;
            if (newZoom == oldZoom) return;

            _currentZoom = newZoom;

            // 3. Define the "Center Point" of the container (Where we zoom towards)
            // Since buttons don't have a mouse cursor, we zoom into the middle of the panel.
            Point centerPoint = new Point(_videoContainer.Width / 2, _videoContainer.Height / 2);

            // 4. Calculate Dimensions
            int newWidth = (int)(_videoContainer.Width * _currentZoom);
            int newHeight = (int)(_videoContainer.Height * _currentZoom);

            // 5. Calculate New Location (Keep center point stable)
            float ratio = newZoom / oldZoom;
            int newX = (int)(centerPoint.X - (centerPoint.X - _videoView.Left) * ratio);
            int newY = (int)(centerPoint.Y - (centerPoint.Y - _videoView.Top) * ratio);

            _videoView.Size = new Size(newWidth, newHeight);
            SetVideoLocation(newX, newY);
        }

        private void SeekSelectedRow()
        {
            if (_grid.SelectedRows.Count == 0) return;
            try
            {
                double secs = Convert.ToDouble(_grid.SelectedRows[0].Cells["Seconds"].Value);
                _mediaPlayer.Time = (long)(secs * 1000);
                if (!_mediaPlayer.IsPlaying) _mediaPlayer.Play();
            }
            catch { }
        }

        public void PlayPause()
        {
            if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause();
            else _mediaPlayer.Play();
        }

        public void SeekBackward() => _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 5000);
        public void SeekForward() => _mediaPlayer.Time += 5000;

        public void ToggleAutoSync()
        {
            if (_syncTimer.Enabled)
            {
                _syncTimer.Stop();
                _isSyncActive = false;
            }
            else
            {
                _syncTimer.Start();
                _isSyncActive = true;
            }
        }

        private void SyncTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying || !_isSyncActive) return;
            if (_track == null || _track.Count == 0) return;

            double videoTime = _mediaPlayer.Time / 1000.0;
            DateTime baseT = _track[0].Time;

            // Simple Linear Search (Fast enough for < 10,000 points)
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

            if (_grid.Rows.Count > bestIdx && _grid.FirstDisplayedScrollingRowIndex != bestIdx)
            {
                _grid.ClearSelection();
                _grid.Rows[bestIdx].Selected = true;
                _grid.FirstDisplayedScrollingRowIndex = bestIdx;
            }

            MoveMarkerTo(bestIdx);
        }

        // --- OPTIMIZED MARKER MOVEMENT ---
        private void MoveMarkerTo(int idx)
        {
            if (!_isSyncActive || _track == null || idx < 0 || idx >= _track.Count) return;

            var p = _track[idx];
            int zone; bool north;
            Utils.ParseZoneString(Commands.SelectedUtmZone, out zone, out north);

            // 1. Calculate Geometry
            Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out double e, out double n, out _, out _);
            double z = p.Ele ?? 0.0;
            double ang = 0.0;

            if (idx < _track.Count - 1)
            {
                Utils.LatLonToUtm(_track[idx + 1].Lat, _track[idx + 1].Lon, zone, north, out double e2, out double n2, out _, out _);
                ang = Math.Atan2(n2 - n, e2 - e);
            }

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // 2. Fast Database Access
            // We swallow errors here because we are in a Timer (don't crash the app if user is busy)
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    // FAST PATH: Update existing marker
                    if (_markerId.IsValid && !_markerId.IsErased)
                    {
                        var blkRef = (BlockReference)tr.GetObject(_markerId, OpenMode.ForWrite);
                        blkRef.Position = new Point3d(e, n, z);
                        blkRef.Rotation = ang;
                        tr.Commit();
                        return;
                    }

                    // SLOW PATH: Create marker (Only runs once)
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    ObjectId markerDefId;

                    if (!bt.Has("GPX_MARKER"))
                    {
                        // Create Arrow (Tip at 0,0)
                        var btr = new BlockTableRecord { Name = "GPX_MARKER" };

                        var head = new Polyline();
                        head.AddVertexAt(0, new Point2d(-0.6, 0.20), 0, 0, 0); // Back Left
                        head.AddVertexAt(1, new Point2d(0.0, 0.0), 0, 0, 0);   // Tip (Center)
                        head.AddVertexAt(2, new Point2d(-0.6, -0.20), 0, 0, 0);// Back Right
                        head.Closed = true;
                        head.Color = Autodesk.AutoCAD.Colors.Color.FromColor(Commands.MarkerColor);

                        btr.AppendEntity(head);
                        bt.UpgradeOpen();
                        markerDefId = bt.Add(btr);
                        tr.AddNewlyCreatedDBObject(btr, true);
                    }
                    else
                    {
                        markerDefId = bt["GPX_MARKER"];
                    }

                    var newRef = new BlockReference(new Point3d(e, n, z), markerDefId);
                    newRef.ScaleFactors = new Scale3d(Commands.MarkerSize);
                    newRef.Rotation = ang;

                    ms.AppendEntity(newRef);
                    tr.AddNewlyCreatedDBObject(newRef, true);
                    _markerId = newRef.ObjectId;

                    tr.Commit();
                }
            }
            catch { }
        }
    }
}
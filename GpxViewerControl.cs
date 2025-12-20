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

        // --- UI COMPONENTS ---
        private Panel _videoContainer;
        private Panel _telemetryBar;
        private Label _lblRouteInfo;
        private Label _lblDistInfo;

        // --- LOGIC VARIABLES ---
        private ObjectId _markerId = ObjectId.Null;
        private const int SYNC_INTERVAL_MS = 100;
        private bool _isSyncActive = false;
        private float _currentZoom = 1.0f;

        // --- NEW DRAG LOGIC VARIABLES ---
        private Point _lastMousePos;
        private bool _isDragging = false;

        // For track history
        private Point3d _lastPos = new Point3d(0, 0, 0);

        public static string LastVideoPath { get; set; }

        public GpxViewerControl()
        {
            InitializeComponent();
            // FIX WHITE BORDERS: Set background immediately
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
            _mediaPlayer.Volume = 0;
        }

        private void SetupUI()
        {
            this.Dock = DockStyle.Fill;
            // Ensure no default padding creates white lines
            this.Padding = new Padding(0);
            this.Margin = new Padding(0);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0),
                BackColor = Color.FromArgb(40, 40, 40) // Ensure background matches
            };

            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 80F)); // Video
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F)); // Telemetry
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Buttons
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F)); // Grid

            this.Controls.Add(mainLayout);

            // =========================================================
            // 1. VIDEO CONTAINER & VIEW
            // =========================================================
            _videoContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            mainLayout.Controls.Add(_videoContainer, 0, 0);

            _videoView = new VideoView
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.None,
                Location = new Point(0, 0),
                Size = new Size(100, 100), // Initial size irrelevant, resized later
                BackColor = Color.Black,
                Margin = new Padding(0)
            };
            _videoContainer.Controls.Add(_videoView);

            // EVENTS: New Pan/Zoom Logic
            _videoView.MouseDown += VideoView_MouseDown;
            _videoView.MouseMove += VideoView_MouseMove;
            _videoView.MouseUp += VideoView_MouseUp;
            // Zoom towards mouse pointer
            _videoView.MouseWheel += (s, e) => ZoomVideo(e.Delta > 0 ? 1.2f : 0.8f, e.Location);

            // Handle container resize
            _videoContainer.Resize += (s, e) =>
            {
                // If not zoomed, always fit to container
                if (_currentZoom <= 1.01f) ResetVideoFit();
                // If zoomed, ensure we don't leave gaps
                else SetVideoLocation(_videoView.Left, _videoView.Top);
            };

            // =========================================================
            // 2. TELEMETRY BAR
            // =========================================================
            _telemetryBar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(55, 55, 55),
                Padding = new Padding(5, 0, 5, 0),
                Margin = new Padding(0)
            };
            mainLayout.Controls.Add(_telemetryBar, 0, 1);

            _lblRouteInfo = new Label
            {
                Text = "NO FILE LOADED",
                ForeColor = Color.LightGray,
                Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular),
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true
            };

            _lblDistInfo = new Label
            {
                Text = "0.00 km",
                ForeColor = Color.Cyan,
                Font = new System.Drawing.Font("Segoe UI", 9.5F, FontStyle.Bold),
                Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = true
            };

            _telemetryBar.Controls.Add(_lblRouteInfo);
            _telemetryBar.Controls.Add(_lblDistInfo);

            // =========================================================
            // 3. CONTROLS
            // =========================================================
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

            // Zoom buttons now zoom to center point
            Point getCenter() => new Point(_videoView.Width / 2, _videoView.Height / 2);

            var buttons = new (string, Action)[]
            {
                ("Import GPX", () => Commands.ImportAndOpen()),
                ("Import Video", ImportVideoFromDialog),
                ("▶/⏸", PlayPause),
                ("⏪5s", SeekBackward),
                ("⏩5s", SeekForward),
                ("SYNC", ToggleAutoSync),
                ("Z+", () => ZoomVideo(1.2f, getCenter())),
                ("Z-", () => ZoomVideo(0.8f, getCenter())),
                ("RESET", () => ZoomVideo(0.0f, Point.Empty))
            };

            foreach (var (text, action) in buttons)
            {
                int w = (text.Length < 3) ? 40 : 75;
                var btn = new Button
                {
                    Text = text,
                    Width = w,
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
                btnPanel.Controls.Add(btn);
            }
            mainLayout.Controls.Add(btnPanel, 0, 2);

            // =========================================================
            // 4. DATA GRID (SUBTLE SELECTION)
            // =========================================================
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToResizeRows = false,
                BackgroundColor = Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0),
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(65, 65, 65)
            };

            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 35);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(50, 50, 50);
            _grid.DefaultCellStyle.ForeColor = Color.White;

            // FIX: Subtle Dark Grey Selection instead of Cyan
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(80, 80, 80);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;

            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) SeekSelectedRow(); };
            mainLayout.Controls.Add(_grid, 0, 3);

            _syncTimer = new Timer { Interval = SYNC_INTERVAL_MS };
            _syncTimer.Tick += SyncTimer_Tick;

            // Force initial layout
            ResetVideoFit();
        }


        // =========================================================
        // FIXED ZOOM & PAN LOGIC
        // =========================================================

        private void VideoView_MouseDown(object sender, MouseEventArgs e)
        {
            // Allow dragging if zoomed in OR middle click
            if ((e.Button == MouseButtons.Left && _currentZoom > 1.01f) || e.Button == MouseButtons.Middle)
            {
                _isDragging = true;
                _lastMousePos = e.Location; // Capture pos relative to control
                _videoView.Cursor = Cursors.SizeAll;
            }
        }

        private void VideoView_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                // Calculate delta (how far mouse moved since last event)
                int deltaX = e.X - _lastMousePos.X;
                int deltaY = e.Y - _lastMousePos.Y;

                // Apply delta to current location
                int newX = _videoView.Left + deltaX;
                int newY = _videoView.Top + deltaY;

                SetVideoLocation(newX, newY);
                // Important: Do NOT update _lastMousePos here. 
                // The control moved under the mouse, so e.Location remains roughly same relative to control.
            }
        }

        private void VideoView_MouseUp(object sender, MouseEventArgs e)
        {
            _isDragging = false;
            _videoView.Cursor = Cursors.Default;
        }

        private void SetVideoLocation(int x, int y)
        {
            // 1. Calculate boundaries (negative values)
            int minX = _videoContainer.Width - _videoView.Width;
            int minY = _videoContainer.Height - _videoView.Height;

            // 2. Enforce boundaries (Cannot drag past edges)
            // If video is wider than container, enforce left/right bounds
            if (_videoView.Width > _videoContainer.Width)
            {
                if (x > 0) x = 0;      // Cannot drag too far right
                if (x < minX) x = minX; // Cannot drag too far left
            }
            else
            {
                // Center horizontally if smaller
                x = (_videoContainer.Width - _videoView.Width) / 2;
            }

            // If video is taller than container, enforce top/bottom bounds
            if (_videoView.Height > _videoContainer.Height)
            {
                if (y > 0) y = 0;      // Cannot drag too far down
                if (y < minY) y = minY; // Cannot drag too far up
            }
            else
            {
                // Center vertically if smaller
                y = (_videoContainer.Height - _videoView.Height) / 2;
            }

            _videoView.Location = new Point(x, y);
        }

        // Zoom towards a specific focal point (e.g., mouse location)
        private void ZoomVideo(float factor, Point focalPoint)
        {
            if (factor == 0.0f) { ResetVideoFit(); return; }

            float newZoom = _currentZoom * factor;
            // Safety Limits
            if (newZoom < 1.0f) newZoom = 1.0f;
            if (newZoom > 10.0f) newZoom = 10.0f; // Allow up to 10x zoom
            if (newZoom == _currentZoom) return;

            float oldZoom = _currentZoom;
            _currentZoom = newZoom;

            // Calculate new size
            int newW = (int)(_videoContainer.Width * _currentZoom);
            int newH = (int)(_videoContainer.Height * _currentZoom);

            // Calculate new location to keep focalPoint stable
            // Math: NewPos = Focal - (Focal - OldPos) * (NewZoom / OldZoom)
            float ratio = newZoom / oldZoom;
            int newX = (int)(focalPoint.X - (focalPoint.X - _videoView.Left) * ratio);
            int newY = (int)(focalPoint.Y - (focalPoint.Y - _videoView.Top) * ratio);

            _videoView.Size = new Size(newW, newH);
            SetVideoLocation(newX, newY);
        }

        private void ResetVideoFit()
        {
            _currentZoom = 1.0f;
            _videoView.Size = _videoContainer.Size;
            _videoView.Location = new Point(0, 0);
        }

        // =========================================================
        // APP LOGIC (Sync, Import, etc.)
        // =========================================================

        public void SetTrack(List<GpxPoint> t)
        {
            _track = t;
            PopulateGrid();
            _lblRouteInfo.Text = "GPX Loaded: " + _track.Count + " points";
            // Enable sync automatically when GPX loads
            if (!_syncTimer.Enabled) ToggleAutoSync();
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
                double gpxTime = (_track[i].Time - baseT).TotalSeconds;
                double diff = Math.Abs(gpxTime - videoTime);
                if (diff < minDiff) { minDiff = diff; bestIdx = i; }
            }

            // Update Grid Selection
            if (_grid.Rows.Count > bestIdx && _grid.FirstDisplayedScrollingRowIndex != bestIdx)
            {
                _grid.ClearSelection();
                _grid.Rows[bestIdx].Selected = true;
                // Ensure the selected row is visible
                _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, bestIdx - 2);
            }

            // Update Telemetry
            double totalDist = 0;
            for (int i = 0; i < bestIdx; i++)
                totalDist += Utils.CalculateDistance(_track[i].Lat, _track[i].Lon, _track[i + 1].Lat, _track[i + 1].Lon);

            string fName = string.IsNullOrEmpty(LastVideoPath) ? "No Video" : Path.GetFileName(LastVideoPath);
            _lblRouteInfo.Text = $"File: {fName} | Pts: {bestIdx + 1}/{_track.Count}";
            _lblDistInfo.Text = $"{totalDist:F3} km";

            MoveMarkerTo(bestIdx);
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
                using (var media = new Media(_libVLC, new Uri(path)))
                    _mediaPlayer.Media = media;
                _mediaPlayer.Play();
                System.Threading.Thread.Sleep(150); // Slightly longer pause for stability
                _mediaPlayer.SetPause(true);
                _lblRouteInfo.Text = "Ready: " + Path.GetFileName(path);
                ResetVideoFit(); // Reset zoom when loading new video
            }
            catch (System.Exception ex)
            {
                Logger.Error($"Error al ingresar el video: {ex}");    
            }
        }

        private void PopulateGrid()
        {
            var tbl = new System.Data.DataTable();
            tbl.Columns.Add("Idx", typeof(int)); tbl.Columns.Add("Lat", typeof(double));
            tbl.Columns.Add("Lon", typeof(double)); tbl.Columns.Add("Ele", typeof(double));
            tbl.Columns.Add("Time", typeof(string)); tbl.Columns.Add("Seconds", typeof(double));
            if (_track == null || _track.Count == 0) { _grid.DataSource = tbl; return; }

            DateTime baseT = _track[0].Time;
            for (int i = 0; i < _track.Count; i++)
            {
                var p = _track[i]; double s = (p.Time - baseT).TotalSeconds;
                var r = tbl.NewRow();
                r["Idx"] = i; r["Lat"] = p.Lat; r["Lon"] = p.Lon; r["Ele"] = p.Ele ?? 0;
                r["Time"] = p.Time.ToString("HH:mm:ss"); r["Seconds"] = Math.Round(s, 1);
                tbl.Rows.Add(r);
            }
            _grid.DataSource = tbl;
        }

        public void PlayPause() { if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause(); else _mediaPlayer.Play(); }
        public void SeekBackward() => _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - 5000);
        public void SeekForward() => _mediaPlayer.Time += 5000;
        public void ToggleAutoSync() { if (_syncTimer.Enabled) { _syncTimer.Stop(); _isSyncActive = false; } else { _syncTimer.Start(); _isSyncActive = true; } }
        private void SeekSelectedRow() { if (_grid.SelectedRows.Count == 0) return; try { double s = Convert.ToDouble(_grid.SelectedRows[0].Cells["Seconds"].Value); _mediaPlayer.Time = (long)(s * 1000); if (!_mediaPlayer.IsPlaying) _mediaPlayer.Play(); } catch (System.Exception ex) { Logger.Error($"Error al buscar en la fila seleccionada: {ex}"); } }

        // Keep your existing MoveMarkerTo method here exactly as it was.
        private void MoveMarkerTo(int idx)
        {
            // Safety check
            if (!_isSyncActive || _track == null || idx < 0 || idx >= _track.Count) return;

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var p = _track[idx];
            int zone; bool north;

            // Auto-Fix Zone if missing (prevents crashes)
            if (string.IsNullOrEmpty(Commands.SelectedUtmZone))
            {
                zone = (int)((p.Lon + 180) / 6) + 1;
                north = p.Lat >= 0;
            }
            else
            {
                Utils.ParseZoneString(Commands.SelectedUtmZone, out zone, out north);
            }

            // 3. Math Calculation (Fast, happens in RAM)
            Utils.LatLonToUtm(p.Lat, p.Lon, zone, north, out double e, out double n, out _, out _);
            double z = p.Ele ?? 0.0;
            Point3d newPos = new Point3d(e, n, z);

            // --- OPTIMIZATION FOR LOW END PC ---
            // If the marker moved less than 10cm, DO NOT redraw. 
            // This saves the GPU from working 60 times a second for no visible change.
            if (_lastPos.DistanceTo(newPos) < 0.1) return;
            _lastPos = newPos;

            double ang = 0.0;
            if (idx < _track.Count - 1)
            {
                Utils.LatLonToUtm(_track[idx + 1].Lat, _track[idx + 1].Lon, zone, north, out double e2, out double n2, out _, out _);
                ang = Math.Atan2(n2 - n, e2 - e);
            }

            try
            {
                // 4. Database Update (Slow, happens on Disk/RAM)
                using (doc.LockDocument())
                {
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        // FAST PATH: Modify existing
                        if (_markerId.IsValid && !_markerId.IsErased)
                        {
                            var blkRef = (BlockReference)tr.GetObject(_markerId, OpenMode.ForWrite);
                            blkRef.Position = newPos;
                            blkRef.Rotation = ang;
                        }
                        else
                        {
                            // SLOW PATH: Create new (Only happens once)
                            var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                            ObjectId markerDefId;
                            if (!bt.Has("GPX_MARKER"))
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
                                markerDefId = bt.Add(btr);
                                tr.AddNewlyCreatedDBObject(btr, true);
                            }
                            else markerDefId = bt["GPX_MARKER"];

                            var newRef = new BlockReference(newPos, markerDefId);
                            newRef.ScaleFactors = new Scale3d(Commands.MarkerSize);
                            newRef.Rotation = ang;
                            ms.AppendEntity(newRef);
                            tr.AddNewlyCreatedDBObject(newRef, true);
                            _markerId = newRef.ObjectId;
                        }

                        tr.Commit();

                        // --- THE MAGIC FIX FOR SMOOTHNESS ---
                        doc.TransactionManager.QueueForGraphicsFlush();
                    }
                }

                // --- THE MAGIC FIX FOR IDLE MOUSE ---
                // Tell Windows: "Repaint the AutoCAD window NOW, even if the mouse is outside."
                doc.Editor.UpdateScreen();

                // On very slow machines, this helps process the Windows Message Queue
                System.Windows.Forms.Application.DoEvents();
            }
            catch (System.Exception ex)
            {
                Logger.Error($"Marker Error: {ex.Message}");
            }
        }
    }
}
namespace GPXVideoTools
{
    partial class GpxViewerControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 1. Stop and Destroy the Timer
                if (_syncTimer != null)
                {
                    _syncTimer.Stop();
                    _syncTimer.Dispose();
                }

                // 2. Stop and Destroy the Video Player
                if (_mediaPlayer != null)
                {
                    try
                    {
                        if (_mediaPlayer.IsPlaying) _mediaPlayer.Stop();
                    }
                    catch { } // Ignore errors during shutdown
                    _mediaPlayer.Dispose();
                }

                // 3. Destroy the VLC Engine
                if (_libVLC != null)
                {
                    _libVLC.Dispose();
                }

                // 4. Standard Designer Cleanup
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Text = "GPX Video Viewer";
        }
    }
}
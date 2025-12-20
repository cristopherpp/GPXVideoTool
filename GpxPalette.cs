using Autodesk.AutoCAD.Windows;
using System;
using System.Drawing;
using Autodesk.AutoCAD.ApplicationServices;

namespace GPXVideoTools
{
    public static class GpxPalette
    {
        private static Autodesk.AutoCAD.Windows.PaletteSet _ps;
        private static GPXVideoTools.GpxViewerControl _control;

        public static GPXVideoTools.GpxViewerControl Control => _control;

        public static void Show()
        {
            try
            {
                // Si ya existe, solo lo mostramos
                if (_ps != null && !_ps.IsDisposed)
                {
                    _ps.Visible = true;
                    return;
                }

                // Primera vez
                _control = new GPXVideoTools.GpxViewerControl();

                _ps = new Autodesk.AutoCAD.Windows.PaletteSet("VISION TRACKER CAD 1.0");

                // Estilos básicos
                _ps.Style = Autodesk.AutoCAD.Windows.PaletteSetStyles.ShowAutoHideButton |
                            Autodesk.AutoCAD.Windows.PaletteSetStyles.ShowCloseButton |
                            Autodesk.AutoCAD.Windows.PaletteSetStyles.Snappable |
                            Autodesk.AutoCAD.Windows.PaletteSetStyles.ShowPropertiesMenu;

                _ps.MinimumSize = new System.Drawing.Size(400, 600);
                _ps.Size = new System.Drawing.Size(460, 850);
                _ps.Dock = Autodesk.AutoCAD.Windows.DockSides.Left;

                // Añadimos el control
                _ps.Add("Visor GPX + Video", _control);

                // Mostrar
                _ps.Visible = true;
            }
            catch (System.Exception ex)
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\nError al abrir panel GPX: {ex.Message}");
            }
        }

        public static void Hide()
        {
            // _ps?.Visible = false;
        }

        // Métodos seguros
        public static void SetTrack(System.Collections.Generic.List<GPXVideoTools.GpxPoint> track) => _control?.SetTrack(track);
        public static void LoadVideo(string path) => _control?.LoadVideo(path);
        public static void ToggleAutoSync() => _control?.ToggleAutoSync();
        public static void PlayPause() => _control?.PlayPause();
        public static void SeekBackward() => _control?.SeekBackward();
        public static void SeekForward() => _control?.SeekForward();
    }
}
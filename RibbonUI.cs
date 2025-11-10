using Autodesk.Windows;
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GPXVideoTools
{
    public static class RibbonUI
    {
        public static RibbonCombo ZoneCombo;
        public static RibbonButton RouteColorButton;
        private static bool _ribbonCreated = false;
        private static bool _updatingZoneCombo = false;

        public static void CreateRibbon()
        {
            if (_ribbonCreated) return; // con esto se evitan ventanas duplicadas
            _ribbonCreated = true;

            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var tab = ribbon.FindTab("GPXVideoTab");
            if (tab == null)
            {
                tab = new RibbonTab() { Title = "GPX Video", Id = "GPXVideoTab" };
                ribbon.Tabs.Add(tab);
            }

            if (tab.Panels.Count > 0) return;

            var src = new RibbonPanelSource() { Title = "GPX Tools" };
            var panel = new RibbonPanel() { Source = src };

            // Crear combo de zonas UTM
            ZoneCombo = new RibbonCombo()
            {
                Text = Commands.SelectedUtmZone,
                Size = RibbonItemSize.Standard
            };

            for (int i = 1; i <= 60; i++) ZoneCombo.Items.Add(new RibbonTextBox { Text = i + "N" });
            for (int i = 1; i <= 60; i++) ZoneCombo.Items.Add(new RibbonTextBox { Text = i + "S" });
            ZoneCombo.Text = Commands.SelectedUtmZone;

            // CAMBIO: antes era ItemChanged → ahora CurrentChanged
            ZoneCombo.CurrentChanged += (s, e) =>
            {
                if (_updatingZoneCombo) return;
                try
                {
                    _updatingZoneCombo = true;
                    Commands.SelectedUtmZone = ZoneCombo.Text;
                    var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    doc?.Editor.WriteMessage($"\nZona UTM actual: {Commands.SelectedUtmZone} seleccionada.");
                }
                finally
                {
                    _updatingZoneCombo = false;
                }
            };

            // Botones
            var importBtn = new RibbonButton()
            {
                Text = "Importar GPX",
                Size = RibbonItemSize.Large,
                ShowText = true,
                ShowImage = true,
                CommandHandler = new RelayCommand((_) =>
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument
                    .SendStringToExecute("GPX_IMPORT_CMD ", true, false, false))
            };

            var play = new RibbonButton
            {
                Text = "Reproducir",
                Size = RibbonItemSize.Standard,
                ShowText = true,
                ShowImage = true,
                CommandHandler = new RelayCommand((_) => Commands.ShowPlayPause())
            };

            var prev = new RibbonButton
            {
                Text = "Previo",
                Size = RibbonItemSize.Standard,
                ShowText = true,
                ShowImage = true,
                CommandHandler = new RelayCommand((_) => Commands.ShowPrev())
            };

            var next = new RibbonButton
            {
                Text = "Siguiente",
                Size = RibbonItemSize.Standard,
                ShowText = true,
                ShowImage = true,
                CommandHandler = new RelayCommand((_) => Commands.ShowNext())
            };

            var sync = new RibbonButton
            {
                Text = "Syncronizar",
                Size = RibbonItemSize.Standard,
                ShowText = true,
                ShowImage = true,
                CommandHandler = new RelayCommand((_) => Commands.ToggleAutoSync())
            };

            var markerBtn = new RibbonButton 
            { 
                Text = "Tamaño Marcador", 
                Size = RibbonItemSize.Standard ,
                ShowText = true,
                ShowImage = true,
                CommandHandler = new RelayCommand((_) => Commands.ToggleAutoSync())
            };

            RouteColorButton = new RibbonButton
            {
                Text = "Color Ruta GPX",
                Size = RibbonItemSize.Standard,
                ShowImage = true,
                ShowText = true,
                CommandHandler = new RelayCommand((_) => Commands.ShowRouteColorDialog())
            };
            UpdateRouteSwatch(Commands.RouteColor);

            // Agregar filas al panel
            src.Items.Add(new RibbonRowPanel() { Items = { ZoneCombo, importBtn } });
            src.Items.Add(new RibbonRowPanel() { Items = { prev, play, next, sync } });
            src.Items.Add(new RibbonRowPanel() { Items = { markerBtn, RouteColorButton } });

            if (!tab.Panels.Any(p => p.Source.Title == "GPX Tools"))
            {
                tab.Panels.Add(panel);
            }
        }

        public static void UpdateRouteSwatch(System.Drawing.Color c)
        {
            RouteColorButton.LargeImage = BitmapToImageSource(CreateSwatch(c)); // Convertir Bitmap a ImageSource
            RouteColorButton.ToolTip = $"Ruta color: RGB({c.R},{c.G},{c.B})";
        }

        private static Bitmap CreateSwatch(System.Drawing.Color c)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new SolidBrush(c))
                    g.FillRectangle(b, 0, 0, 16, 16);
                g.DrawRectangle(Pens.Black, 0, 0, 15, 15);
            }
            return bmp;
        }

        // Conversión de Bitmap → ImageSource (requerido para AutoCAD 2024)
        private static ImageSource BitmapToImageSource(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}

using System.Reflection;

using Autodesk.AutoCAD.Windows;
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
        public static RibbonButton SyncButton;
        public static RibbonButton ShowPanelButton;

        private static bool _ribbonCreated = false;
        private static bool _updatingZoneCombo = false;

        public static void CreateRibbon()
        {
            if (_ribbonCreated) return;
            _ribbonCreated = true;

            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var tab = ribbon.FindTab("GPXVideoTab") ?? new RibbonTab
            {
                Title = "GPX Video",
                Id = "GPXVideoTab"
            };

            if (tab.Id == "GPXVideoTab" && !ribbon.Tabs.Contains(tab))
                ribbon.Tabs.Add(tab);

            if (tab.Panels.Any(p => p.Source?.Title == "GPX Tools")) return;

            var src = new RibbonPanelSource { Title = "GPX Tools" };
            var panel = new RibbonPanel { Source = src };

            // === BOTONES ===
            var ShowPanelButton = CreateButton("Mostrar Panel", () => GpxPalette.Show(), large: true, "Abrir panel lateral con video + tabla");
            SyncButton = CreateButton("Sync ON/OFF", () => Commands.ToggleAutoSync(), large: false);
            RouteColorButton = CreateButton("Color Ruta", () => Commands.ShowRouteColorDialog(), large: false);

            UpdateRouteSwatch(Commands.RouteColor);

            src.Items.Add(new RibbonRowPanel { Items = { ShowPanelButton } });
            src.Items.Add(new RibbonRowPanel { Items = { SyncButton, RouteColorButton } });

            tab.Panels.Add(panel);
            tab.IsActive = true;
        }
        
        private static RibbonButton CreateButton(string text, Action action, bool large = false, string tooltip = "")
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                LargeImage = GetIcon(text),
                CommandHandler = new RelayCommand(_ => action())
            };
        }

        private static RibbonButton CreateButton(string text, string command, bool large = false, string tooltip = "")
        {
            var btn = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                LargeImage = GetIcon(text),
                CommandHandler = new RelayCommand(_ =>
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument
                        .SendStringToExecute(command + " ", true, false, false))
            };
            if (!string.IsNullOrEmpty(tooltip)) btn.ToolTip = tooltip;
            return btn;
        }

        private static ImageSource GetIcon(string name)
        {
            // Puedes cargar .png reales aquí después
            return BitmapToImageSource(CreateIconBitmap(name));
        }

        private static Bitmap CreateIconBitmap(string text)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.DrawString(text.Substring(0, 1), new Font("Arial", 14, FontStyle.Bold), System.Drawing.Brushes.White, 4, 4);
            }
            return bmp;
        }

        private static void ZoneCombo_CurrentChanged(object sender, EventArgs e)
        {
            if (_updatingZoneCombo || ZoneCombo.Current == null) return;
            _updatingZoneCombo = true;
            try
            {
                Commands.SelectedUtmZone = ZoneCombo.Text;
                var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\nZona UTM: {Commands.SelectedUtmZone}");
            }
            finally { _updatingZoneCombo = false; }
        }

        public static void UpdateRouteSwatch(System.Drawing.Color c)
        {
            if (RouteColorButton != null)
            {
                RouteColorButton.LargeImage = BitmapToImageSource(CreateSwatch(c));
                RouteColorButton.ToolTip = $"Color Ruta: RGB({c.R},{c.G},{c.B})";
            }
        }

        private static Bitmap CreateSwatch(System.Drawing.Color c)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new SolidBrush(c)) g.FillRectangle(b, 2, 2, 28, 28);
                g.DrawRectangle(Pens.Black, 2, 2, 27, 27);
            }
            return bmp;
        }

        private static ImageSource BitmapToImageSource(Bitmap bmp)
        {
            IntPtr h = bmp.GetHbitmap();
            try { return Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
            finally { DeleteObject(h); }
        }

        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    }
}
using Autodesk.Windows;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input; // Needed for ICommand
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GPXVideoTools
{
    public static class RibbonUI
    {
        public static RibbonButton ShowPanelButton;
        public static RibbonButton SyncButton;
        public static RibbonButton RouteColorButton;

        private static bool _ribbonCreated = false;

        public static void CreateRibbon()
        {
            if (_ribbonCreated) return;

            // Ribbon check
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            _ribbonCreated = true;

            // 1. Create Tab
            var tab = ribbon.FindTab("GPXVideoTracker") ?? new RibbonTab
            {
                Title = "VIDEO TRACKER",
                Id = "GPXVideoTracker"
            };

            if (!ribbon.Tabs.Contains(tab))
                ribbon.Tabs.Add(tab);

            // Avoid duplicating panels
            if (tab.Panels.Any(p => p.Source.Title == "VIDEO TOOLS")) return;

            var src = new RibbonPanelSource { Title = "VIDEO TOOLS" };
            var panel = new RibbonPanel { Source = src };

            // 2. Create Buttons (Use static fields)
            ShowPanelButton = CreateButton("Mostrar Panel", () => GpxPalette.Show(), large: true, "Abre el visor de video y GPX");
            //SyncButton = CreateButton("Sync ON/OFF", () => Commands.ToggleAutoSync(), large: false, "Activa la sincronización");
            //RouteColorButton = CreateButton("Color Ruta", () => Commands.ShowRouteColorDialog(), large: false, "Cambia el color de la polilínea");

            UpdateRouteSwatch(Commands.RouteColor);

            // 3. Add to Ribbon
            src.Items.Add(new RibbonRowPanel { Items = { ShowPanelButton } });

            // Add Separator and Row 2
            src.Items.Add(new RibbonSeparator());
            src.Items.Add(new RibbonRowPanel { Items = { SyncButton, RouteColorButton } });

            tab.Panels.Add(panel);
            tab.IsActive = true;
        }

        private static RibbonButton CreateButton(string text, Action action, bool large = false, string tooltip = "")
        {
            var btn = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = GetIcon(text),
                Image = GetIcon(text),
                CommandHandler = new RelayCommand(action), // Uses helper below
                ToolTip = tooltip
            };
            return btn;
        }

        public static void UpdateRouteSwatch(System.Drawing.Color c)
        {
            if (RouteColorButton != null)
            {
                var icon = BitmapToImageSource(CreateSwatch(c));
                RouteColorButton.Image = icon;
                RouteColorButton.LargeImage = icon;
                RouteColorButton.ToolTip = $"Color Ruta: RGB({c.R},{c.G},{c.B})";
            }
        }

        // --- GRAPHICS HELPERS ---

        private static ImageSource GetIcon(string name)
        {
            return BitmapToImageSource(CreateIconBitmap(name));
        }

        private static Bitmap CreateIconBitmap(string text)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(System.Drawing.Brushes.DarkSlateGray, 2, 2, 28, 28);

                var letter = string.IsNullOrEmpty(text) ? "?" : text.Substring(0, 1).ToUpper();
                var font = new Font("Arial", 14, FontStyle.Bold);
                var size = g.MeasureString(letter, font);
                g.DrawString(letter, font, System.Drawing.Brushes.White, 16 - (size.Width / 2), 16 - (size.Height / 2));
            }
            return bmp;
        }

        private static Bitmap CreateSwatch(System.Drawing.Color c)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new SolidBrush(c)) g.FillRectangle(b, 0, 0, 15, 15);
                g.DrawRectangle(Pens.Black, 0, 0, 15, 15);
            }
            return bmp;
        }

        private static ImageSource BitmapToImageSource(Bitmap bmp)
        {
            IntPtr h = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    h, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(h); }
        }

        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    }

    // --- REQUIRED HELPER CLASS FOR COMMANDS ---
    public class RelayCommand : ICommand
    {
        private readonly Action _action;
        public RelayCommand(Action action) { _action = action; }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _action?.Invoke();
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }
}
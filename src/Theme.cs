using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BlackNote
{
    /// <summary>Colors of the window chrome (menus, dialogs, status bar) and dark-mode helpers.</summary>
    internal static class Theme
    {
        public static readonly Color Window = Color.FromArgb(32, 32, 32);
        public static readonly Color Menu = Color.FromArgb(43, 43, 43);
        public static readonly Color Hover = Color.FromArgb(66, 66, 66);
        public static readonly Color Border = Color.FromArgb(72, 72, 72);
        public static readonly Color Status = Color.FromArgb(37, 37, 38);
        public static readonly Color Text = Color.FromArgb(222, 222, 222);
        public static readonly Color Disabled = Color.FromArgb(115, 115, 115);
        public static readonly Color Input = Color.FromArgb(50, 50, 50);
        public static readonly Color Button = Color.FromArgb(58, 58, 58);
        public static readonly Color Accent = Color.FromArgb(64, 156, 255);

        public static float Scale { get; private set; } = 1f;

        /// <summary>Scales a 96-DPI pixel value to the current DPI.</summary>
        public static int S(int value) => (int)Math.Round(value * Scale);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        // Undocumented uxtheme exports (Windows 10 1903+), used by Explorer itself for dark mode.
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int mode);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

        public static void Init()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; } catch { }
            try { SetPreferredAppMode(1); } catch { } // 1 = AllowDark
            ToolStripManager.Renderer = new DarkRenderer();
        }

        public static void DarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) // DWMWA_USE_IMMERSIVE_DARK_MODE
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);         // same attribute before Windows 10 20H1
            }
            catch { }
        }

        public static void DarkScrollBars(IntPtr hwnd)
        {
            try
            {
                AllowDarkModeForWindow(hwnd, true);
                SetWindowTheme(hwnd, "DarkMode_Explorer", null);
            }
            catch { }
        }

        public static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));

        public static bool IsLight(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 140;

        public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Menu;
        public override Color MenuItemPressedGradientMiddle => Theme.Menu;
        public override Color MenuItemPressedGradientEnd => Theme.Menu;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuStripGradientBegin => Theme.Menu;
        public override Color MenuStripGradientEnd => Theme.Menu;
        public override Color ToolStripDropDownBackground => Theme.Menu;
        public override Color ImageMarginGradientBegin => Theme.Menu;
        public override Color ImageMarginGradientMiddle => Theme.Menu;
        public override Color ImageMarginGradientEnd => Theme.Menu;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color CheckBackground => Theme.Menu;
        public override Color CheckSelectedBackground => Theme.Hover;
        public override Color CheckPressedBackground => Theme.Hover;
        public override Color ToolStripGradientBegin => Theme.Menu;
        public override Color ToolStripGradientMiddle => Theme.Menu;
        public override Color ToolStripGradientEnd => Theme.Menu;
        public override Color ToolStripBorder => Theme.Menu;
        public override Color ButtonSelectedHighlight => Theme.Hover;
        public override Color ButtonSelectedBorder => Theme.Hover;
        public override Color ButtonSelectedGradientBegin => Theme.Hover;
        public override Color ButtonSelectedGradientMiddle => Theme.Hover;
        public override Color ButtonSelectedGradientEnd => Theme.Hover;
        public override Color ButtonPressedHighlight => Theme.Border;
        public override Color ButtonPressedBorder => Theme.Border;
        public override Color ButtonPressedGradientBegin => Theme.Border;
        public override Color ButtonPressedGradientMiddle => Theme.Border;
        public override Color ButtonPressedGradientEnd => Theme.Border;
        public override Color ButtonCheckedHighlight => Theme.Hover;
        public override Color ButtonCheckedGradientBegin => Theme.Hover;
        public override Color ButtonCheckedGradientMiddle => Theme.Hover;
        public override Color ButtonCheckedGradientEnd => Theme.Hover;
        public override Color StatusStripGradientBegin => Theme.Status;
        public override Color StatusStripGradientEnd => Theme.Status;
        public override Color GripDark => Theme.Border;
        public override Color GripLight => Theme.Status;
        public override Color OverflowButtonGradientBegin => Theme.Menu;
        public override Color OverflowButtonGradientMiddle => Theme.Menu;
        public override Color OverflowButtonGradientEnd => Theme.Menu;
    }

    internal sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item == null || e.Item.Enabled ? Theme.Text : Theme.Disabled;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            var g = e.Graphics;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Text, Math.Max(1.5f, 1.7f * Theme.Scale)))
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.80f, r.Top + r.Height * 0.30f),
                });
            }
            g.SmoothingMode = old;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using var pen = new Pen(Theme.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var r = e.Item.ContentRectangle;
            using var pen = new Pen(Theme.Border);
            if (e.Vertical)
            {
                int x = r.Left + r.Width / 2;
                e.Graphics.DrawLine(pen, x, r.Top + 3, x, r.Bottom - 3);
            }
            else
            {
                int y = r.Top + r.Height / 2;
                e.Graphics.DrawLine(pen, r.Left + Theme.S(4), y, r.Right - Theme.S(4), y);
            }
        }

        protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
        {
            base.OnRenderLabelBackground(e);
            if (e.Item is ToolStripStatusLabel && Equals(e.Item.Tag, "segment"))
            {
                using var pen = new Pen(Theme.Border);
                e.Graphics.DrawLine(pen, 0, 3, 0, e.Item.Height - 4);
            }
        }
    }
}

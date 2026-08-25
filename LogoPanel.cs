using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Svg;

namespace QuickLaunchMenuWinForms
{
    /// <summary>Reserved 240×60 top-right logo slot. Renders Assets\logo.svg if present; otherwise
    /// draws a dashed placeholder so the reserved space is visible before a logo is supplied.</summary>
    public class LogoPanel : Panel
    {
        private Image? _logo;
        private FileSystemWatcher? _watcher;

        public LogoPanel()
        {
            Width = 240;
            Height = 60;
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            LoadLogo();
            StartWatcher();
            Disposed += (s, e) => _watcher?.Dispose();
        }

        private void StartWatcher()
        {
            try
            {
                var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
                Directory.CreateDirectory(assetsDir);
                _watcher = new FileSystemWatcher(assetsDir, "logo.svg") { EnableRaisingEvents = true };
                _watcher.Created += (s, e) => SafeReload();
                _watcher.Changed += (s, e) => SafeReload();
            }
            catch
            {
                // Not critical — Reload() can still be called manually.
            }
        }

        private void SafeReload()
        {
            if (IsDisposed) return;
            try { if (IsHandleCreated) BeginInvoke(new Action(Reload)); }
            catch { /* form may be closing */ }
        }

        /// <summary>Call again if a logo.svg is dropped in while the app is running.</summary>
        public void Reload()
        {
            _logo?.Dispose();
            _logo = null;
            LoadLogo();
            Invalidate();
        }

        private void LoadLogo()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "logo.svg"),
                Path.Combine(AppContext.BaseDirectory, "logo.svg"),
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var doc = SvgDocument.Open<SvgDocument>(path);
                    _logo = doc.Draw(Width, Height);
                    return;
                }
                catch
                {
                    // Fall through to the placeholder if the SVG can't be parsed/rendered.
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_logo != null)
            {
                g.DrawImage(_logo, 0, 0, Width, Height);
                return;
            }

            using (var pen = new Pen(Theme.Border) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            using (var brush = new SolidBrush(Theme.MutedText))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString($"логотип\n{Width}×{Height}, SVG", Font, brush, ClientRectangle, format);
            }
        }
    }
}

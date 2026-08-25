using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickLaunchMenuWinForms
{
    /// <summary>A plain Panel with a rounded, bordered background — used to mimic a Windows context-menu card.</summary>
    public class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; } = 8;
        public Color BorderColor { get; set; } = Color.FromArgb(200, 200, 200);
        public Color FillColor { get; set; } = Color.White;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var parentColor = Parent?.BackColor ?? SystemColors.Control;
            using (var eraseBrush = new SolidBrush(parentColor))
            {
                g.FillRectangle(eraseBrush, ClientRectangle);
            }

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRect(rect, CornerRadius))
            using (var fillBrush = new SolidBrush(FillColor))
            using (var borderPen = new Pen(BorderColor))
            {
                g.FillPath(fillBrush, path);
                g.DrawPath(borderPen, path);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

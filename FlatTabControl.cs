using System.Drawing;
using System.Windows.Forms;

namespace QuickLaunchMenuWinForms
{
    /// <summary>
    /// A TabControl that paints itself entirely (UserPaint) instead of relying on the native visual style —
    /// the stock TabControl always leaves a light strip behind/around the tabs in dark mode because Windows
    /// draws that chrome itself regardless of BackColor. Based on the approach from
    /// github.com/mcka-dev/Dark-Mode-WinForms (FlatTabControl.cs), simplified for this app (no close buttons).
    /// </summary>
    public class FlatTabControl : TabControl
    {
        public Color BorderColor { get; set; } = Theme.Border;
        public Color SelectedTabColor { get; set; } = Theme.Surface;
        public Color SelectedForeColor { get; set; } = Theme.Text;
        public Color TabColor { get; set; } = Theme.Background;
        public Color TabForeColor { get; set; } = Theme.MutedText;

        public FlatTabControl()
        {
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(130, 34);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            using (var backBrush = new SolidBrush(Theme.Background))
            {
                g.FillRectangle(backBrush, ClientRectangle);
            }

            for (var i = 0; i < TabCount; i++)
            {
                DrawTab(g, i);
            }

            // Border around the page area, just below the tab strip.
            var pageRect = ClientRectangle;
            pageRect.Y = ItemSize.Height + 2;
            pageRect.Height -= pageRect.Y + 1;
            pageRect.Width -= 1;
            using (var borderPen = new Pen(BorderColor))
            {
                g.DrawRectangle(borderPen, pageRect);
            }
        }

        private void DrawTab(Graphics g, int index)
        {
            var rect = GetTabRect(index);
            var selected = SelectedIndex == index;

            using (var brush = new SolidBrush(selected ? SelectedTabColor : TabColor))
            {
                g.FillRectangle(brush, rect);
            }
            using (var borderPen = new Pen(BorderColor))
            {
                g.DrawRectangle(borderPen, rect);
            }

            TextRenderer.DrawText(g, TabPages[index].Text, Font, rect,
                selected ? SelectedForeColor : TabForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

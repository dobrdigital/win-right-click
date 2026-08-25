using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    /// <summary>Static mock-up of how the desktop right-click menu will look, kept in sync with the live entry list.
    /// Clicking a row here highlights the matching row in the list on the left, and vice versa (see Highlight).</summary>
    public class MenuPreviewControl : Panel
    {
        private readonly RoundedPanel _card;
        private readonly FlowLayoutPanel _rows;
        private Panel? _highlightedRow;

        /// <summary>Fired when a clickable row (a real link, not a group header) is clicked.</summary>
        public event Action<MenuNode>? NodeClicked;

        public MenuPreviewControl()
        {
            BackColor = SystemColors.ControlLight;
            Padding = new Padding(12);

            var caption = new Label
            {
                Text = Localization.T("Предпросмотр (правый клик на рабочем столе)", "Preview (right-click)"),
                Dock = DockStyle.Top,
                Height = 34,
                ForeColor = SystemColors.GrayText,
                Font = new Font("Segoe UI", 8F)
            };

            _rows = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(4)
            };

            _card = new RoundedPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0) };
            _card.Controls.Add(_rows);

            Controls.Add(_card);
            Controls.Add(caption);
        }

        public void SetEntries(List<MenuNode> tree, List<MenuNode>? extensions = null)
        {
            _highlightedRow = null;
            _rows.SuspendLayout();
            _rows.Controls.Clear();

            if (tree.Count == 0 && (extensions == null || extensions.Count == 0))
            {
                _rows.Controls.Add(BuildRow(Localization.T("Пока пусто", "Nothing yet"), null, null, indent: 0, bold: false, chevron: false, italic: true));
            }
            else
            {
                foreach (var node in tree)
                {
                    if (node.IsGroup)
                    {
                        // The group header itself has no matching row in the list (only its children do).
                        _rows.Controls.Add(BuildRow(node.DisplayName, null, null, indent: 0, bold: true, chevron: true));
                        foreach (var child in node.Children)
                        {
                            var icon = child.Link != null
                                ? IconHelper.GetIconFor(string.IsNullOrWhiteSpace(child.Link.IconPath) ? child.Link.TargetPath : child.Link.IconPath)
                                : null;
                            _rows.Controls.Add(BuildRow(child.DisplayName, icon, child, indent: 18, bold: false, chevron: false));
                        }
                    }
                    else
                    {
                        var icon = node.Link != null
                            ? IconHelper.GetIconFor(string.IsNullOrWhiteSpace(node.Link.IconPath) ? node.Link.TargetPath : node.Link.IconPath)
                            : null;
                        _rows.Controls.Add(BuildRow(node.DisplayName, icon, node, indent: 0, bold: false, chevron: false));
                    }
                }

                if (extensions != null)
                {
                    foreach (var ext in extensions)
                    {
                        var label = "🔌 " + ext.DisplayName + (ext.Extension != null && ext.Extension.IsDisabled ? Localization.T(" (выкл)", " (off)") : "");
                        _rows.Controls.Add(BuildRow(label, null, null, indent: 0, bold: false, chevron: false,
                            italic: ext.Extension != null && ext.Extension.IsDisabled));
                    }
                }
            }

            _rows.ResumeLayout();
        }

        /// <summary>Highlights the row matching this node (by reference — the same MenuNode instances the
        /// list's rows are tagged with), or clears the highlight when passed null.</summary>
        public void Highlight(MenuNode? node)
        {
            if (_highlightedRow != null)
            {
                _highlightedRow.BackColor = Theme.Surface;
                _highlightedRow = null;
            }

            if (node == null) return;

            foreach (Control control in _rows.Controls)
            {
                if (control is Panel row && ReferenceEquals(row.Tag, node))
                {
                    row.BackColor = Theme.SurfaceAlt;
                    _highlightedRow = row;
                    (row.Parent as ScrollableControl)?.ScrollControlIntoView(row);
                    break;
                }
            }
        }

        private Panel BuildRow(string text, Icon? icon, MenuNode? node, int indent, bool bold, bool chevron, bool italic = false)
        {
            var rowWidth = System.Math.Max(80, Width - 24 - indent);
            // Rows are (re)created from scratch on every SetEntries call — e.g. after adding/removing an
            // entry — long after the one-time startup Theme.Apply() sweep already ran, so they don't get
            // themed by that pass. Set the real colors explicitly here instead of relying on it, otherwise
            // a rebuilt row briefly shows up with the default light background / black text.
            var row = new Panel
            {
                Height = 24,
                Width = rowWidth,
                Margin = new Padding(indent, 1, 0, 1),
                Tag = node,
                Cursor = node != null ? Cursors.Hand : Cursors.Default,
                BackColor = Theme.Surface
            };

            void WireClick(Control c)
            {
                if (node == null) return;
                c.Click += (s, e) => NodeClicked?.Invoke(node);
            }

            var iconBox = new PictureBox
            {
                Size = new Size(16, 16),
                Location = new Point(2, 4),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Cursor = row.Cursor,
                BackColor = Theme.Surface
            };
            if (icon != null) iconBox.Image = icon.ToBitmap();
            row.Controls.Add(iconBox);
            WireClick(iconBox);

            var style = bold ? FontStyle.Bold : (italic ? FontStyle.Italic : FontStyle.Regular);
            var label = new Label
            {
                Text = text,
                Location = new Point(22, 3),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, style),
                ForeColor = italic ? Theme.MutedText : Theme.Text,
                Cursor = row.Cursor
            };
            row.Controls.Add(label);
            WireClick(label);

            if (chevron)
            {
                var chevronLabel = new Label
                {
                    Text = "▸",
                    Dock = DockStyle.Right,
                    Width = 18,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Theme.MutedText
                };
                row.Controls.Add(chevronLabel);
            }

            WireClick(row);
            return row;
        }
    }
}

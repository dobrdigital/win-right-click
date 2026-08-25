using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickLaunchMenuWinForms
{
    /// <summary>
    /// Dark theme applied throughout the app. Classic WinForms has no built-in dark mode (that only
    /// landed in .NET 9+), so this walks the control tree and sets colors by hand, plus native calls
    /// for the parts Windows itself draws (title bar, scrollbars, ListView header). Technique verified
    /// against github.com/mcka-dev/Dark-Mode-WinForms — SetWindowTheme("DarkMode_Explorer") applied to
    /// every control (not just ListView) is what actually themes scrollbars; ListView additionally needs
    /// OwnerDraw + DrawColumnHeader because native header theming alone doesn't reliably kick in.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(24, 24, 26);
        public static readonly Color Surface = Color.FromArgb(32, 32, 35);
        public static readonly Color SurfaceAlt = Color.FromArgb(42, 42, 46);
        public static readonly Color Border = Color.FromArgb(70, 70, 76);
        public static readonly Color Text = Color.FromArgb(230, 230, 232);
        public static readonly Color MutedText = Color.FromArgb(150, 150, 156);
        public static readonly Color Accent = Color.FromArgb(70, 140, 255);

        private static bool _appModeSet;

        /// <summary>Call once, as the very first thing in Main() — before any window/control is created.
        /// Calling this after controls already exist leaves their native chrome (scrollbars especially)
        /// stuck in light mode even though everything else themes correctly.</summary>
        public static void InitializeAppMode()
        {
            if (_appModeSet) return;
            _appModeSet = true;
            SafeRun(() => SetPreferredAppMode(AllowDark));
        }

        public static void Apply(Control root)
        {
            InitializeAppMode();

            ApplyRecursive(root);

            if (root is Form form)
            {
                form.BackColor = Background;
                if (form.IsHandleCreated) DarkTitleBar(form.Handle);
                else form.HandleCreated += (s, e) => DarkTitleBar(form.Handle);
            }
        }

        private static void ApplyRecursive(Control control)
        {
            // SetWindowTheme alone themes list/tree/header chrome; scrollbars specifically need the
            // undocumented AllowDarkModeForWindow opt-in (same API Windows Terminal/Notepad use) on top
            // of it — applied to every control with a handle, not just scrollable ones.
            RunWhenHandleReady(control, () =>
            {
                AllowDarkModeForWindow(control.Handle, true);
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            });

            switch (control)
            {
                case RoundedPanel rounded:
                    rounded.FillColor = SurfaceAlt;
                    rounded.BorderColor = Border;
                    rounded.BackColor = Surface;
                    break;

                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.MouseOverBackColor = SurfaceAlt;
                    button.FlatAppearance.MouseDownBackColor = Border;
                    button.BackColor = Surface;
                    button.ForeColor = Text;
                    break;

                case TextBox textBox:
                    textBox.BackColor = SurfaceAlt;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case ComboBox comboBox:
                    comboBox.BackColor = SurfaceAlt;
                    comboBox.ForeColor = Text;
                    comboBox.FlatStyle = FlatStyle.Flat;
                    RunWhenHandleReady(comboBox, () => SetWindowTheme(comboBox.Handle, "DarkMode_CFD", null));
                    break;

                case ListView listView:
                    ThemeListView(listView);
                    break;

                case TabControl tabControl:
                    tabControl.BackColor = Background;
                    tabControl.ForeColor = Text;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    label.ForeColor = label.ForeColor == SystemColors.GrayText ? MutedText : Text;
                    break;

                case PictureBox pictureBox:
                    pictureBox.BackColor = Color.Transparent;
                    break;

                default:
                    control.BackColor = Background;
                    control.ForeColor = Text;
                    break;
            }

            foreach (Control child in control.Controls)
            {
                ApplyRecursive(child);
            }
        }

        // Value is null until the ListView's own column-resize callback (Details view only) replaces it —
        // see ThemeListView and RefreshColumnLayout.
        private static readonly ConditionalWeakTable<ListView, object> ThemedListViews = new ConditionalWeakTable<ListView, object>();

        /// <summary>Call after repopulating a themed ListView's Items — e.g. right after a reload/refresh.
        /// Adding or removing rows can change whether the vertical scrollbar is showing, which changes how
        /// much width is actually left for columns; nothing else notices that on its own (unlike an actual
        /// window Resize/Layout), so columns can end up too wide and a spurious horizontal scrollbar
        /// appears. Deferred via BeginInvoke so it runs after the item-count change has fully settled.</summary>
        public static void RefreshColumnLayout(ListView listView)
        {
            if (listView.IsHandleCreated && ThemedListViews.TryGetValue(listView, out var value) && value is System.Action refresh)
            {
                listView.BeginInvoke(refresh);
            }
        }

        private static void ThemeListView(ListView listView)
        {
            // WinForms' Control.ControlCollection enumerator can pick up controls added to a parent
            // *while it's being enumerated* (adding the scrollbar below does exactly that one level up),
            // so this can legitimately be re-entered for the same ListView — guard the whole thing.
            if (ThemedListViews.TryGetValue(listView, out _)) return;
            ThemedListViews.Add(listView, null!);

            listView.BackColor = Surface;
            listView.ForeColor = Text;
            listView.BorderStyle = BorderStyle.FixedSingle;

            // Native GridLines paint full-height lines across the whole control — including the empty
            // area below the last row — in an untintable system color. We draw our own per-row lines
            // instead (below), which only appear where rows actually exist.
            listView.GridLines = false;

            if (listView.View == View.Details)
            {
                listView.OwnerDraw = true;

                // Plain ListView doesn't double-buffer itself, so repeated Invalidate() calls (e.g. on
                // every mouse move for hover feedback) visibly flicker. DoubleBuffered is protected on
                // Control with no public ListView equivalent — flipping it via reflection is the standard,
                // widely-used fix for exactly this.
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(listView, true, null);

                listView.DrawColumnHeader += (s, e) =>
                {
                    using (var backBrush = new SolidBrush(SurfaceAlt))
                    using (var foreBrush = new SolidBrush(Text))
                    using (var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                    {
                        e.Graphics.FillRectangle(backBrush, e.Bounds);
                        var textRect = e.Bounds;
                        textRect.X += 4;
                        e.Graphics.DrawString(e.Header?.Text, listView.Font, foreBrush, textRect, format);
                    }
                };

                // Keep every column proportional to the ListView's own width — so the table always fills
                // 100% of the available space (no horizontal scrollbar, no dead white strip past the last
                // column) and every column keeps its relative size as the window is resized. The initial
                // ratios come from whatever widths each panel already set via Columns.Add(...); dragging a
                // column border by hand recaptures the ratios so the new proportion sticks on later resizes
                // instead of snapping back. Recomputed on every layout pass (not just Resize) and once more
                // after the window actually settles, since the ListView's real width isn't always final yet
                // when the handle is first created (e.g. while it's still inside a not-yet-laid-out TabPage).
                //
                // A panel can mark any number of its trailing columns as fixed-width "action" columns
                // (Tag = "fixed", e.g. the "⋯" open-folder and "!" search buttons) — those are excluded
                // from all of this and just keep whatever width they were given; the column right before
                // the first fixed one becomes the flexible filler instead of the true last column.
                float[]? columnRatios = null;
                var isAdjustingColumns = false;

                bool IsFixedActionColumn(int index) =>
                    index >= 0 && index < listView.Columns.Count &&
                    string.Equals(listView.Columns[index].Tag as string, "fixed", System.StringComparison.Ordinal) &&
                    index >= ProportionalCount();

                int ProportionalCount()
                {
                    var count = listView.Columns.Count;
                    while (count > 0 && string.Equals(listView.Columns[count - 1].Tag as string, "fixed", System.StringComparison.Ordinal))
                        count--;
                    return count;
                }

                bool IsNonDraggable(int index) => index >= ProportionalCount() - 1;

                void ResizeColumnsProportionally()
                {
                    if (isAdjustingColumns || !listView.IsHandleCreated || listView.Columns.Count == 0) return;

                    var proportionalCount = ProportionalCount();
                    if (proportionalCount == 0) return;
                    var fixedWidth = 0;
                    for (var i = proportionalCount; i < listView.Columns.Count; i++) fixedWidth += listView.Columns[i].Width;

                    if (columnRatios == null)
                    {
                        var total = 0;
                        for (var i = 0; i < proportionalCount; i++) total += listView.Columns[i].Width;
                        if (total <= 0) return;
                        columnRatios = new float[proportionalCount];
                        for (var i = 0; i < proportionalCount; i++)
                            columnRatios[i] = listView.Columns[i].Width / (float)total;
                    }

                    var availableWidth = listView.ClientSize.Width - fixedWidth;
                    if (availableWidth <= 0) return;

                    isAdjustingColumns = true;
                    try
                    {
                        var assigned = 0;
                        for (var i = 0; i < proportionalCount; i++)
                        {
                            int width;
                            if (i == proportionalCount - 1) width = System.Math.Max(40, availableWidth - assigned);
                            else
                            {
                                width = System.Math.Max(40, (int)(availableWidth * columnRatios[i]));
                                assigned += width;
                            }
                            if (listView.Columns[i].Width != width) listView.Columns[i].Width = width;
                        }
                    }
                    finally { isAdjustingColumns = false; }

                    // Columns always fill exactly the available width now, so a horizontal scrollbar is
                    // never actually needed — but shrinking the window can make the native control flash
                    // one anyway for a frame while it re-measures. Force it off explicitly every time.
                    if (listView.IsHandleCreated) SafeRun(() => ShowScrollBar(listView.Handle, SB_HORZ, false));
                }

                // Replace the reentrancy-guard placeholder with the real callback so RefreshColumnLayout
                // can trigger a recompute from outside (e.g. after a panel repopulates Items).
                ThemedListViews.Remove(listView);
                ThemedListViews.Add(listView, (System.Action)ResizeColumnsProportionally);

                listView.Layout += (s, e) => ResizeColumnsProportionally();
                listView.Resize += (s, e) => ResizeColumnsProportionally();

                // Every column except the flexible filler (and the fixed action column, if any) can still
                // be dragged by hand — but right after any manual resize, the filler is snapped to whatever
                // width makes the total exactly match the ListView's width again, so dragging a column can
                // never leave a horizontal scrollbar or a dead unpainted strip.
                listView.ColumnWidthChanging += (s, e) =>
                {
                    if (isAdjustingColumns) return;
                    if (IsNonDraggable(e.ColumnIndex))
                    {
                        e.NewWidth = listView.Columns[e.ColumnIndex].Width;
                        e.Cancel = true;
                    }
                };
                listView.ColumnWidthChanged += (s, e) =>
                {
                    if (isAdjustingColumns || IsNonDraggable(e.ColumnIndex)) return;

                    var proportionalCount = ProportionalCount();
                    var flexIndex = proportionalCount - 1;
                    var fixedWidth = 0;
                    for (var i = proportionalCount; i < listView.Columns.Count; i++) fixedWidth += listView.Columns[i].Width;

                    isAdjustingColumns = true;
                    try
                    {
                        var used = 0;
                        for (var i = 0; i < flexIndex; i++) used += listView.Columns[i].Width;
                        var flexColumn = listView.Columns[flexIndex];
                        var remaining = System.Math.Max(40, listView.ClientSize.Width - fixedWidth - used);
                        if (flexColumn.Width != remaining) flexColumn.Width = remaining;
                    }
                    finally { isAdjustingColumns = false; }

                    // Recapture ratios from the resulting widths so this new split persists across
                    // future window resizes instead of snapping back to the old proportions.
                    var total = 0;
                    for (var i = 0; i < proportionalCount; i++) total += listView.Columns[i].Width;
                    if (total <= 0) return;
                    columnRatios ??= new float[proportionalCount];
                    for (var i = 0; i < proportionalCount; i++)
                        columnRatios[i] = listView.Columns[i].Width / (float)total;
                };
                RunWhenHandleReady(listView, () =>
                {
                    ResizeColumnsProportionally();
                    listView.BeginInvoke(new System.Action(ResizeColumnsProportionally));
                });

                // Owner-drawing takes over painting completely, which also silently drops the native
                // "row lights up under the cursor" hover feedback — track it ourselves so the list
                // doesn't feel dead between hovering and actually clicking.
                var hoveredIndex = -1;
                void InvalidateRow(int index)
                {
                    if (index < 0 || index >= listView.Items.Count) return;
                    listView.Invalidate(listView.Items[index].Bounds);
                }
                listView.MouseMove += (s, e) =>
                {
                    var item = listView.GetItemAt(e.X, e.Y);
                    var idx = item?.Index ?? -1;

                    var overActionButton = false;
                    if (item != null)
                    {
                        for (var col = ProportionalCount(); col < listView.Columns.Count; col++)
                        {
                            if (col < item.SubItems.Count && item.SubItems[col].Bounds.Contains(e.Location))
                            {
                                overActionButton = true;
                                break;
                            }
                        }
                    }
                    listView.Cursor = overActionButton ? Cursors.Hand : Cursors.Default;

                    if (idx == hoveredIndex) return;
                    var previous = hoveredIndex;
                    hoveredIndex = idx;
                    InvalidateRow(previous);
                    InvalidateRow(idx);
                };
                listView.MouseLeave += (s, e) =>
                {
                    if (hoveredIndex == -1) return;
                    var previous = hoveredIndex;
                    hoveredIndex = -1;
                    InvalidateRow(previous);
                };

                listView.DrawItem += (s, e) => { /* rendering happens per cell in DrawSubItem below */ };
                listView.DrawSubItem += (s, e) =>
                {
                    var selected = e.Item.Selected;
                    var hovered = !selected && e.ItemIndex == hoveredIndex;
                    using (var backBrush = new SolidBrush(selected ? Accent : (hovered ? SurfaceAlt : Surface)))
                    {
                        e.Graphics.FillRectangle(backBrush, e.Bounds);
                    }

                    var textColor = selected ? Color.White : e.Item.ForeColor;

                    if (IsFixedActionColumn(e.ColumnIndex))
                    {
                        // A small per-row button (e.g. "⋯" to open the containing folder) rather than a
                        // data cell — center it instead of the usual left-aligned text.
                        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, listView.Font, e.Bounds,
                            hovered || selected ? textColor : MutedText,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        return;
                    }

                    var textBounds = e.Bounds;
                    textBounds.X += 4;
                    textBounds.Width = System.Math.Max(0, textBounds.Width - 4);

                    if (e.ColumnIndex == 0 && e.Item.ImageList != null && e.Item.ImageIndex >= 0)
                    {
                        var iconSize = e.Item.ImageList.ImageSize;
                        var iconY = e.Bounds.Top + System.Math.Max(0, (e.Bounds.Height - iconSize.Height) / 2);
                        e.Item.ImageList.Draw(e.Graphics, textBounds.X, iconY, e.Item.ImageIndex);
                        textBounds.X += iconSize.Width + 4;
                        textBounds.Width = System.Math.Max(0, textBounds.Width - iconSize.Width - 4);
                    }

                    TextRenderer.DrawText(e.Graphics, e.SubItem.Text, listView.Font, textBounds, textColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                };
            }

            RunWhenHandleReady(listView, () =>
            {
                SetWindowTheme(listView.Handle, "DarkMode_ItemsView", null);

                // The column header is a separate child window (SysHeader32) — themeing the ListView
                // itself doesn't reliably reach it, so it needs its own explicit theme too.
                var headerHandle = SendMessage(listView.Handle, LVM_GETHEADER, System.IntPtr.Zero, System.IntPtr.Zero);
                if (headerHandle != System.IntPtr.Zero)
                {
                    SetWindowTheme(headerHandle, "DarkMode_ItemsView", null);
                }
            });
        }

        private static void RunWhenHandleReady(Control control, System.Action action)
        {
            if (control.IsHandleCreated) SafeRun(action);
            else control.HandleCreated += (s, e) => SafeRun(action);
        }

        private static void SafeRun(System.Action action)
        {
            try { action(); }
            catch { /* purely cosmetic native calls — never worth crashing over */ }
        }

        // -------------------- Native bits --------------------

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(System.IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

        [DllImport("user32.dll")]
        private static extern System.IntPtr SendMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);

        private const int LVM_GETHEADER = 0x1000 + 31;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(System.IntPtr hWnd, int wBar, bool bShow);

        private const int SB_HORZ = 0;

        // Undocumented since Windows 10 1809 — same APIs Windows Terminal/Notepad use for real dark
        // scrollbars and other native chrome that SetWindowTheme alone doesn't retint. Ordinal-bound,
        // so this can legitimately fail on older/unusual Windows builds — always call through SafeRun.
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        private static extern bool AllowDarkModeForWindow(System.IntPtr hWnd, bool allow);

        private const int AllowDark = 1;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        private static void DarkTitleBar(System.IntPtr hwnd)
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref enabled, sizeof(int));
            }
        }
    }
}

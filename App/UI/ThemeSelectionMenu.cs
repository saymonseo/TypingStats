using TypingStats.Core;

namespace TypingStats.App.UI;

/// <summary>Owned by the main form; hiding a popup must not dispose it during item-click handling.</summary>
internal sealed class ThemeSelectionMenu : ContextMenuStrip
{
    public ThemeSelectionMenu(Func<AppTheme> selected, Action<AppTheme> choose)
    {
        Theme.Menu(this);
        foreach (var (preference, caption) in new[] { (AppTheme.Light, "Светлая"), (AppTheme.Dark, "Тёмная"), (AppTheme.System, "Как в Windows") })
        {
            var item = new ToolStripMenuItem(caption) { Tag = preference };
            item.Click += (_, _) => choose(preference); Items.Add(item);
        }
        Opening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in Items) item.Checked = (AppTheme)item.Tag! == selected();
        };
        // No Closed -> Dispose handler. WinForms still accesses the dropdown after Closed.
    }

    /// <summary>Exercise the same managed mouse handlers as a user click, only inside this test popup.</summary>
    internal void VerifyMouseSelect(AppTheme preference)
    {
        if (!Visible) throw new InvalidOperationException("Verification requires a visible test popup.");
        var item = Items.Cast<ToolStripMenuItem>().Single(i => (AppTheme)i.Tag! == preference);
        var bounds = item.Bounds; var x = bounds.Left + bounds.Width / 2; var y = bounds.Top + bounds.Height / 2;
        OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
    }
}

using System.Drawing.Drawing2D;

namespace TypingStats.App.UI;

internal sealed class ThemedComboBox : ComboBox
{
    public ThemedComboBox() { DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0 ? Theme.BlueSoft : Theme.Card);
        e.Graphics.FillRectangle(brush,e.Bounds);
        var text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
        TextRenderer.DrawText(e.Graphics,text,Font,Rectangle.Inflate(e.Bounds,-Theme.P(4),0),Enabled?Theme.Ink:Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if(m.Msg==0x0f) {using var g=Graphics.FromHwnd(Handle);Arrow(g);}
        else if(m.Msg is 0x317 or 0x318 && m.WParam!=0) {using var g=Graphics.FromHdc(m.WParam);Arrow(g);}
    }
    private void Arrow(Graphics g)
    {
        var r = new Rectangle(Math.Max(0,Width-Theme.P(24)),0,Theme.P(24),Height);
        using var fill=new SolidBrush(Theme.Card);g.FillRectangle(fill,r);
        using var ink=new SolidBrush(Theme.Muted);var x=r.Left+r.Width/2;var y=Height/2;
        g.SmoothingMode=SmoothingMode.AntiAlias;g.FillPolygon(ink,[new Point(x-Theme.P(4),y-Theme.P(2)),new Point(x+Theme.P(4),y-Theme.P(2)),new Point(x,y+Theme.P(3))]);
        using var pen=new Pen(Focused?Theme.Blue:Theme.Line);g.DrawRectangle(pen,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
    }
}

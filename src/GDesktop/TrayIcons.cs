using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GDesktop;

internal static class TrayIcons
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr icon);

    public static Icon Create(Color badge, bool paused=false)
    {
        using var bitmap=new Bitmap(32,32);
        using var graphics=Graphics.FromImage(bitmap);
        graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var background=new SolidBrush(Color.FromArgb(22,135,119));
        graphics.FillEllipse(background,1,1,29,29);
        using var pen=new Pen(Color.White,2.8f);
        graphics.DrawEllipse(pen,7,6,12,12);graphics.DrawLine(pen,17,17,23,24);
        using var border=new SolidBrush(Color.White);graphics.FillEllipse(border,18,18,14,14);
        using var fill=new SolidBrush(badge);graphics.FillEllipse(fill,20,20,10,10);
        if(paused){graphics.FillRectangle(Brushes.White,23,22,1.6f,6);graphics.FillRectangle(Brushes.White,26,22,1.6f,6);}
        var handle=bitmap.GetHicon();
        try{using var borrowed=Icon.FromHandle(handle);return (Icon)borrowed.Clone();}
        finally{DestroyIcon(handle);}
    }
}

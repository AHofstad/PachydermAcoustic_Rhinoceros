using System;
using System.IO;
using Eto.Drawing;
using Eto.Forms;

// Minimal EtoPlot shim compiled against Rhino 8's Eto 2.11.x.
// The NuGet ScottPlot.Eto package targets an older, unsigned Eto 2.8 assembly,
// which conflicts with Rhino 8's signed Eto 2.11. Defining EtoPlot directly in
// the project against Rhino's Eto avoids that assembly-identity conflict.
namespace ScottPlot.Eto
{
  public class EtoPlot : Drawable
  {
    public EtoPlot()
    {
      Paint += OnPaint;
    }


    public Plot Plot { get; } = new Plot();


    private void OnPaint(object sender, PaintEventArgs e)
    {
      var width = Math.Max(1, Width);
      var height = Math.Max(1, Height);
      try
      {
        var img = Plot.GetImage(width, height);
        var bytes = img.GetImageBytes();
        using (var ms = new MemoryStream(bytes))
        using (var bmp = new Bitmap(ms))
          e.Graphics.DrawImage(bmp, new RectangleF(0, 0, width, height));
      }
      catch
      {
      }
    }


    public void Refresh()
    {
      Invalidate();
    }


    public void Update()
    {
      Invalidate();
    }
  }
}

using System.Windows;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

namespace Flake.Rendering;

/// <summary>A snowman assembled from three existing snowballs.</summary>
internal sealed class Snowman
{
    private static readonly SolidColorBrush CoalBrush = CreateBrush(43, 53, 65, 245);
    private static readonly WpfPen ScarfPen = CreatePen(177, 70, 83, 245, 6);
    private static readonly WpfPen NosePen = CreatePen(232, 145, 65, 245, 3.5);

    public Snowball Bottom { get; }
    public Snowball Middle { get; }
    public Snowball Top { get; }

    public Snowman(Snowball bottom, Snowball middle, Snowball top)
    {
        Bottom = bottom;
        Middle = middle;
        Top = top;
        AlignParts();
    }

    public bool Contains(Snowball ball) =>
        ReferenceEquals(Bottom, ball) ||
        ReferenceEquals(Middle, ball) ||
        ReferenceEquals(Top, ball);

    public void AlignParts()
    {
        Middle.X = Bottom.X;
        Middle.Y = Bottom.Y - Bottom.Radius - Middle.Radius + 1.0;
        Top.X = Middle.X;
        Top.Y = Middle.Y - Middle.Radius - Top.Radius + 1.0;
        Middle.VelocityX = Bottom.VelocityX;
        Middle.VelocityY = Bottom.VelocityY;
        Top.VelocityX = Bottom.VelocityX;
        Top.VelocityY = Bottom.VelocityY;
    }

    public void Translate(double offsetX, double offsetY)
    {
        Bottom.X += offsetX;
        Bottom.Y += offsetY;
        Middle.X += offsetX;
        Middle.Y += offsetY;
        Top.X += offsetX;
        Top.Y += offsetY;
    }

    public void DrawDetails(DrawingContext drawingContext)
    {
        var head = new WpfPoint(Top.X, Top.Y);
        var eyeOffset = Math.Max(4, Top.Radius * 0.28);
        var eyeY = head.Y - Top.Radius * 0.12;

        drawingContext.DrawEllipse(CoalBrush, null,
            new WpfPoint(head.X - eyeOffset, eyeY), 2.4, 2.4);
        drawingContext.DrawEllipse(CoalBrush, null,
            new WpfPoint(head.X + eyeOffset, eyeY), 2.4, 2.4);
        drawingContext.DrawLine(NosePen,
            new WpfPoint(head.X + 1, head.Y + 2),
            new WpfPoint(head.X + Top.Radius + 8, head.Y + 5));

        var scarfY = Middle.Y - Middle.Radius * 0.25;
        drawingContext.DrawLine(ScarfPen,
            new WpfPoint(Middle.X - Middle.Radius * 0.72, scarfY),
            new WpfPoint(Middle.X + Middle.Radius * 0.72, scarfY));

        drawingContext.DrawEllipse(CoalBrush, null,
            new WpfPoint(Middle.X, Middle.Y + 1), 2.8, 2.8);
        drawingContext.DrawEllipse(CoalBrush, null,
            new WpfPoint(Middle.X, Middle.Y + Middle.Radius * 0.42), 2.8, 2.8);
        drawingContext.DrawEllipse(CoalBrush, null,
            new WpfPoint(Bottom.X, Bottom.Y - Bottom.Radius * 0.1), 3.1, 3.1);
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue, byte alpha)
    {
        var brush = new SolidColorBrush(WpfColor.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static WpfPen CreatePen(byte red, byte green, byte blue, byte alpha, double thickness)
    {
        var pen = new WpfPen(CreateBrush(red, green, blue, alpha), thickness);
        pen.Freeze();
        return pen;
    }
}

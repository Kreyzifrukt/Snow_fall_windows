namespace Flake.Rendering;

/// <summary>A hand-formed, draggable snowball in the desktop simulation.</summary>
internal sealed class Snowball
{
    public double X { get; set; }
    public double Y { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double Radius { get; set; }
    public double Rotation { get; set; }
    public bool IsBroken { get; set; }

    public double Mass => Math.Max(1, Radius * Radius);
}

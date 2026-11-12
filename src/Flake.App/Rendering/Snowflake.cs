namespace Flake.Rendering;

internal sealed class Snowflake
{
    public double X { get; set; }
    public double Y { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double Radius { get; init; }
    public double FallSpeed { get; init; }
    public double Drift { get; init; }
    public double Phase { get; init; }
    public double Opacity { get; init; }
}

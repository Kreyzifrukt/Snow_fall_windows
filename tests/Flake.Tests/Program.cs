using System.Windows;
using Flake.Rendering;

namespace Flake.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("window profile follows resize", WindowProfileFollowsResize),
            ("snow falls when its window moves", WindowMoveReleasesAccumulatedSnow),
            ("fast flake path lands on window surface", FastFlakePathLands),
            ("desktop profile survives viewport resize", DesktopProfileSurvivesResize),
            ("screen bounds convert through WPF transform", ScreenBoundsConvertThroughTransform),
            ("stopping emission keeps existing particles active", StopEmissionKeepsExistingParticlesActive)
        };

        var failures = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void WindowProfileFollowsResize()
    {
        var patch = new WindowSnowPatch(new IntPtr(1), new Rect(100, 100, 400, 300));
        patch.DepositTop(300, 8);
        var before = patch.GetTopHeight(300);
        patch.UpdateBounds(new Rect(100, 100, 600, 300));
        AssertNear(before, patch.GetTopHeight(400), 0.8, "resized top profile");
    }

    private static void WindowMoveReleasesAccumulatedSnow()
    {
        var patch = new WindowSnowPatch(new IntPtr(2), new Rect(100, 100, 400, 300));
        patch.DepositTop(300, 8);
        var releasedAmount = 0.0;
        patch.SnowDropped += (_, _, amount) => releasedAmount += amount;
        patch.UpdateBounds(new Rect(180, 140, 400, 300));
        Assert(releasedAmount > 0, "moving a window must release snow at the old screen coordinates");
        Assert(patch.GetTopHeight(300) < 0.01, "the moved window must not carry old snow with it");
    }

    private static void FastFlakePathLands()
    {
        var patch = new WindowSnowPatch(new IntPtr(3), new Rect(100, 100, 400, 300));
        var landed = patch.TryLandFlake(200, 80, 350, 130, 3);
        Assert(landed, "a diagonal path crossing the top edge must register a landing");
        Assert(patch.GetTopHeight(300) > 0, "landing must deposit snow at the crossing location");
    }

    private static void DesktopProfileSurvivesResize()
    {
        var surface = new SnowSurface();
        surface.Resize(400, 300);
        surface.Accumulate(200, 3);
        var before = surface.GetHeightAt(200);
        surface.Resize(800, 300);
        AssertNear(before, surface.GetHeightAt(400), 0.5, "resized desktop profile");
    }

    private static void ScreenBoundsConvertThroughTransform()
    {
        var local = WindowSnowSurface.ConvertScreenBounds(
            new Rect(-1920, 0, 1280, 1024),
            point => new Point((point.X + 1920) / 2, point.Y / 1.5));
        AssertNear(0, local.Left, 0.01, "transformed left");
        AssertNear(0, local.Top, 0.01, "transformed top");
        AssertNear(640, local.Width, 0.01, "transformed width");
        AssertNear(1024 / 1.5, local.Height, 0.01, "transformed height");
    }

    private static void StopEmissionKeepsExistingParticlesActive()
    {
        var state = new SnowfallState();
        Assert(state.ToggleGeneration() == false, "generation must turn off");
        Assert(state.ShouldAdvanceExistingParticles, "existing particles must continue updating");
        Assert(!state.ShouldRespawnLandedParticles, "landed particles must not respawn after stopping");
        Assert(state.TogglePause(), "pause remains a separate control");
        Assert(!state.ShouldAdvanceExistingParticles, "an explicit pause must still stop updates");
    }

    private static void AssertNear(double expected, double actual, double tolerance, string label)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{label}: expected {expected:F2}, got {actual:F2} (tolerance {tolerance:F2})");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

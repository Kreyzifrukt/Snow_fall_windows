namespace Flake.Rendering;

/// <summary>Separates a simulation pause from the control that stops new particle emission.</summary>
internal sealed class SnowfallState
{
    public bool IsPaused { get; private set; }
    public bool IsGenerating { get; private set; } = true;
    public bool ShouldAdvanceExistingParticles => !IsPaused;
    public bool ShouldRespawnLandedParticles => IsGenerating;

    public bool TogglePause() => IsPaused = !IsPaused;

    public bool ToggleGeneration() => IsGenerating = !IsGenerating;
}

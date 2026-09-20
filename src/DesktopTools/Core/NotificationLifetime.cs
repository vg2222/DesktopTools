namespace DesktopTools.Core;

/// <summary>Wait for input after appearance, then count only visible, unhovered time.</summary>
public sealed class NotificationLifetime
{
    private uint? inputAtAppearance;
    private bool active;
    public TimeSpan Remaining { get; private set; }
    public NotificationLifetime(TimeSpan duration, uint? input) => Reset(duration, input);
    public void Reset(TimeSpan duration, uint? input) { Remaining = duration; inputAtAppearance = input; active = false; }
    public bool Advance(TimeSpan elapsed, uint? input, bool paused)
    {
        if (paused) return false;
        if (!active)
        {
            if (!input.HasValue) return false;
            if (!inputAtAppearance.HasValue) { inputAtAppearance = input; return false; }
            if (input == inputAtAppearance) return false;
            active = true; return false;
        }
        Remaining -= elapsed;
        return Remaining <= TimeSpan.Zero;
    }
}

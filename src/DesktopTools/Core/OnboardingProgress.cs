namespace DesktopTools.Core;

public enum SetupStatus { NotStarted, InProgress, Completed, Skipped }

public sealed class OnboardingProgress
{
    public SetupStatus Status { get; set; }
    public int Step { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShouldResume => Status is SetupStatus.NotStarted or SetupStatus.InProgress;
    public void Validate(int stepCount)
    {
        if (!Enum.IsDefined(Status)) Status = SetupStatus.NotStarted;
        Step = Math.Clamp(Step, 0, Math.Max(0, stepCount - 1));
    }
}

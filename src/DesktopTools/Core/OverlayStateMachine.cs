namespace DesktopTools.Core;

public enum OverlayState { Hidden, Draw, Interact, Capture }

public sealed class OverlayStateMachine
{
    public OverlayState State { get; private set; } = OverlayState.Hidden;
    public OverlayState PreviousState { get; private set; } = OverlayState.Hidden;
    public void ToggleDraw()
    {
        State = State switch { OverlayState.Hidden or OverlayState.Interact => OverlayState.Draw, OverlayState.Draw => OverlayState.Interact, _ => State };
    }
    public void Hide() { State = OverlayState.Hidden; PreviousState = OverlayState.Hidden; }
    public void BeginCapture()
    {
        if (State == OverlayState.Capture) return;
        PreviousState = State; State = OverlayState.Capture;
    }
    public void EndCapture() { if (State == OverlayState.Capture) State = PreviousState; }
    public void Disable() => Hide();
}

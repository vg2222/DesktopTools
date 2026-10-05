namespace DesktopTools.Core;
public sealed class RedactionReviewState(bool requireScan = false)
{
    private long revision;
    private readonly bool mandatory = requireScan;
    private bool closed, requested = requireScan;
    public bool IsScanning { get; private set; }
    public bool NeedsScan { get; private set; } = requireScan;
    /// <summary>Only the automatic review (the user asked for every capture to be checked before output) makes a missing check block Copy and Save.</summary>
    public bool RequiresScanToExport => mandatory && NeedsScan;
    public bool HasUnresolved => Findings.Count > 0;
    public bool CanExport => !closed && !IsScanning && !RequiresScanToExport && !HasUnresolved;
    public IReadOnlyList<SensitiveFinding> Findings { get; private set; } = [];
    public long BeginScan()
    {
        if (closed) throw new ObjectDisposedException(nameof(RedactionReviewState));
        requested = true; IsScanning = true; NeedsScan = true; Findings = []; return ++revision;
    }
    public bool CompleteScan(long scanRevision, IReadOnlyList<SensitiveFinding> findings)
    {
        if (closed || scanRevision != revision || !IsScanning) return false;
        Findings = findings.ToArray(); IsScanning = false; NeedsScan = false; return true;
    }
    public void FailScan(long scanRevision)
    {
        if (closed || scanRevision != revision) return;
        IsScanning = false; NeedsScan = true; Findings = [];
    }
    public void Invalidate() { revision++; IsScanning = false; Findings = []; NeedsScan = requested; }
    public void Resolve(IReadOnlyCollection<Guid> ids) => Findings = Findings.Where(f => !ids.Contains(f.Id)).ToArray();
    public void SkipRemaining() { revision++; IsScanning = false; NeedsScan = false; Findings = []; }
    public void Close() { closed = true; revision++; IsScanning = false; Findings = []; }
}

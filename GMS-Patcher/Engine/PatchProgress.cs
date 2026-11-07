using System;

namespace GMS_UTML_Patcher.Engine;

public class PatchProgress
{
    public int Percentage { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TargetId { get; set; }

    public PatchProgress() { }

    public PatchProgress(int percentage, string status, string? targetId = null)
    {
        Percentage = percentage;
        Status = status;
        TargetId = targetId;
    }
}


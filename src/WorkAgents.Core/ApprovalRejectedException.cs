namespace WorkAgents.Core;

/// <summary>承認却下によりrunを継続してはいけないことを示す。</summary>
public sealed class ApprovalRejectedException : InvalidOperationException
{
    public ApprovalRejectedException(ApprovalRequest request)
        : base($"Approval rejected for tool '{request.Tool}'.")
    {
        Request = request;
    }

    public ApprovalRequest Request { get; }
}
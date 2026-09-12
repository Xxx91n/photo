namespace PhotoPrivacy.Core.Audit;

public sealed class NoopAuditLogger : IAuditLogger
{
    public ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

namespace PhotoPrivacy.Core.Audit;

public interface IAuditLogger
{
    ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

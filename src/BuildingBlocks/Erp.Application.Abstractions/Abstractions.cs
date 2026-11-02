namespace Erp.Application.Abstractions;

using Erp.SharedKernel;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICurrentUser
{
    UserId? UserId { get; }
    bool IsAuthenticated { get; }
}

public interface ICompanyContext
{
    CompanyId CompanyId { get; }
    bool HasCompany { get; }
}

public interface IAuditRequestContext
{
    string CorrelationId { get; }
    string? IpAddress { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class UnavailableCompanyContext : ICompanyContext
{
    public CompanyId CompanyId => throw new InvalidOperationException("An active company context has not been configured.");
    public bool HasCompany => false;
}

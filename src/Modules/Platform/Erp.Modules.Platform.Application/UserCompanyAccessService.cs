using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

public sealed class UserCompanyAccessService(
    IUserCompanyAccessStore store,
    IClock clock,
    IAuditTrail audit) : IUserCompanyAccessService, ICompanyAccessAuthorizer
{
    public Task<bool> CanAccessCompanyAsync(UserId userId, CompanyId companyId, CancellationToken cancellationToken) =>
        store.HasActiveAccessAsync(userId, companyId, cancellationToken);

    public async Task<IReadOnlyList<CompanyResponse>> ListCompaniesForUserAsync(
        UserId userId,
        CancellationToken cancellationToken) =>
        (await store.ListCompaniesForUserAsync(userId, cancellationToken))
            .Select(MapCompany)
            .ToArray();

    public async Task<IReadOnlyList<UserResponse>> ListUsersForCompanyAsync(
        CompanyId companyId,
        CancellationToken cancellationToken) =>
        (await store.ListUsersForCompanyAsync(companyId, cancellationToken))
            .Select(MapUser)
            .ToArray();

    public async Task<UserCompanyAccessResponse?> GrantAsync(
        UserId actorUserId,
        UserId targetUserId,
        CompanyId companyId,
        CancellationToken cancellationToken)
    {
        if (!await store.UserExistsAsync(targetUserId, cancellationToken) ||
            !await store.CompanyExistsAsync(companyId, cancellationToken))
            return null;

        var existing = await store.GetAsync(targetUserId, companyId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == UserCompanyAccessStatus.Active)
                throw new InvalidOperationException("The user already has access to this company.");

            existing.Activate(actorUserId, clock.UtcNow);
            await audit.RecordAsync("CompanyAccess", "user.company-access.restored", "UserCompanyAccess",
                $"{targetUserId.Value}:{companyId.Value}", targetUserId.Value.ToString(), AuditOutcome.Succeeded,
                new Dictionary<string, object?> { ["TargetUserId"] = targetUserId.Value, ["CompanyId"] = companyId.Value }, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return MapAccess(existing);
        }

        var access = UserCompanyAccess.Create(targetUserId, companyId, actorUserId, clock.UtcNow);
        await store.AddAsync(access, cancellationToken);
        await audit.RecordAsync("CompanyAccess", "user.company-access.granted", "UserCompanyAccess",
            $"{targetUserId.Value}:{companyId.Value}", targetUserId.Value.ToString(), AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["TargetUserId"] = targetUserId.Value, ["CompanyId"] = companyId.Value }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return MapAccess(access);
    }

    public async Task<UserCompanyAccessResponse?> RevokeAsync(
        UserId actorUserId,
        UserId targetUserId,
        CompanyId companyId,
        CancellationToken cancellationToken)
    {
        var access = await store.GetAsync(targetUserId, companyId, cancellationToken);
        if (access is null)
            return null;

        access.Deactivate(actorUserId, clock.UtcNow);
        await audit.RecordAsync("CompanyAccess", "user.company-access.revoked", "UserCompanyAccess",
            $"{targetUserId.Value}:{companyId.Value}", targetUserId.Value.ToString(), AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["TargetUserId"] = targetUserId.Value, ["CompanyId"] = companyId.Value }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return MapAccess(access);
    }

    public async Task<UserCompanyAccessResponse?> RestoreAsync(
        UserId actorUserId,
        UserId targetUserId,
        CompanyId companyId,
        CancellationToken cancellationToken)
    {
        var access = await store.GetAsync(targetUserId, companyId, cancellationToken);
        if (access is null)
            return null;

        access.Activate(actorUserId, clock.UtcNow);
        await audit.RecordAsync("CompanyAccess", "user.company-access.restored", "UserCompanyAccess",
            $"{targetUserId.Value}:{companyId.Value}", targetUserId.Value.ToString(), AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["TargetUserId"] = targetUserId.Value, ["CompanyId"] = companyId.Value }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return MapAccess(access);
    }

    private static CompanyResponse MapCompany(Company company) =>
        new(company.Id.Value, company.Code, company.Name, company.Status, company.CreatedAt, company.ModifiedAt);

    private static UserResponse MapUser(User user) =>
        new(user.Id.Value, user.UserName, user.Email, user.Status, user.CreatedAt, user.ModifiedAt);

    private static UserCompanyAccessResponse MapAccess(UserCompanyAccess access) =>
        new(
            access.UserId.Value,
            access.CompanyId.Value,
            access.Status,
            access.CreatedAt,
            access.CreatedBy.Value,
            access.ModifiedAt,
            access.ModifiedBy?.Value);
}

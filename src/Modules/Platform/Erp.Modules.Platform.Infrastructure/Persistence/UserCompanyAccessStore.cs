using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class UserCompanyAccessStore(PlatformDbContext db) : IUserCompanyAccessStore
{
    public Task<UserCompanyAccess?> GetAsync(UserId userId, CompanyId companyId, CancellationToken ct) =>
        db.UserCompanyAccesses.SingleOrDefaultAsync(
            access => access.UserId == userId && access.CompanyId == companyId,
            ct);

    public Task<bool> UserExistsAsync(UserId userId, CancellationToken ct) =>
        db.Users.AnyAsync(user => user.Id == userId, ct);

    public Task<bool> CompanyExistsAsync(CompanyId companyId, CancellationToken ct) =>
        db.Companies.AnyAsync(company => company.Id == companyId, ct);

    public async Task<IReadOnlyList<Company>> ListCompaniesForUserAsync(UserId userId, CancellationToken ct) =>
        await db.UserCompanyAccesses
            .Where(access => access.UserId == userId && access.Status == UserCompanyAccessStatus.Active)
            .Join(db.Users.Where(user => user.Status == UserStatus.Active),
                access => access.UserId,
                user => user.Id,
                (access, _) => access)
            .Join(db.Companies.Where(company => company.Status == CompanyStatus.Active),
                access => access.CompanyId,
                company => company.Id,
                (_, company) => company)
            .AsNoTracking()
            .OrderBy(company => company.Code)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<User>> ListUsersForCompanyAsync(CompanyId companyId, CancellationToken ct) =>
        await db.UserCompanyAccesses
            .Where(access => access.CompanyId == companyId && access.Status == UserCompanyAccessStatus.Active)
            .Join(db.Users.Where(user => user.Status == UserStatus.Active),
                access => access.UserId,
                user => user.Id,
                (_, user) => user)
            .AsNoTracking()
            .OrderBy(user => user.UserName)
            .ToListAsync(ct);

    public async Task<bool> HasActiveAccessAsync(UserId userId, CompanyId companyId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(user => user.Id == userId && user.Status == UserStatus.Active, ct) ||
            !await db.Companies.AnyAsync(company => company.Id == companyId && company.Status == CompanyStatus.Active, ct))
            return false;

        return await db.UserCompanyAccesses.AnyAsync(
            access => access.UserId == userId &&
                      access.CompanyId == companyId &&
                      access.Status == UserCompanyAccessStatus.Active,
            ct);
    }

    public Task AddAsync(UserCompanyAccess access, CancellationToken ct)
    {
        db.UserCompanyAccesses.Add(access);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

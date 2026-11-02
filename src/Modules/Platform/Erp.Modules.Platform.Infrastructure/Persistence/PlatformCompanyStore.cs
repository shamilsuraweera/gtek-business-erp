using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformCompanyStore(PlatformDbContext dbContext) : ICompanyStore
{
    public Task AddAsync(Company company, CancellationToken cancellationToken)
    {
        dbContext.Companies.Add(company);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Companies.AsNoTracking().OrderBy(company => company.Code).ToListAsync(cancellationToken);

    public Task<Company?> GetByIdAsync(CompanyId id, CancellationToken cancellationToken) =>
        dbContext.Companies.SingleOrDefaultAsync(company => company.Id == id, cancellationToken);

    public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.Companies.SingleOrDefaultAsync(company => company.Code == code, cancellationToken);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.Companies.AnyAsync(company => company.Code == code, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

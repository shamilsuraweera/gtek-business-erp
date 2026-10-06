using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class NumberSequenceStore(PlatformDbContext db) : INumberSequenceStore
{
    public Task AddAsync(NumberSequence sequence, CancellationToken cancellationToken)
    {
        db.Set<NumberSequence>().Add(sequence);
        return Task.CompletedTask;
    }

    public Task<NumberSequence?> GetAsync(CompanyId companyId, NumberSequenceId id, CancellationToken cancellationToken) =>
        db.Set<NumberSequence>().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);

    public Task<NumberSequence?> GetByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken)
    {
        var normalized = NumberSequence.NormalizeCode(code);
        return db.Set<NumberSequence>().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<NumberSequence>> ListAsync(CompanyId companyId, CancellationToken cancellationToken) =>
        await db.Set<NumberSequence>().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<string> ReserveNextAsync(CompanyId companyId, string code, CancellationToken cancellationToken)
    {
        var normalized = NumberSequence.NormalizeCode(code);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM platform.\"NumberSequences\" WHERE \"CompanyId\" = {companyId.Value} AND \"Code\" = {normalized} FOR UPDATE",
            cancellationToken);

        var sequence = await db.Set<NumberSequence>().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Code == normalized, cancellationToken)
            ?? throw new KeyNotFoundException("The number sequence was not found.");
        var number = sequence.Reserve();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return number;
    }
}

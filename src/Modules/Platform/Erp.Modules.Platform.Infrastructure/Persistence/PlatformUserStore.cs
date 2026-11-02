using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformUserStore(PlatformDbContext dbContext) : IUserStore
{
    public async Task AddAsync(User user, string passwordHash, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);
        dbContext.UserCredentials.Add(new UserCredential(user.Id, passwordHash));
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Users.AsNoTracking().OrderBy(user => user.UserName).ToListAsync(cancellationToken);

    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.UserName == userName, cancellationToken);

    public Task<bool> ExistsByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.UserName == userName, cancellationToken);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken);

    public Task<string?> GetPasswordHashAsync(UserId id, CancellationToken cancellationToken) =>
        dbContext.UserCredentials.Where(credential => credential.UserId == id)
            .Select(credential => credential.PasswordHash)
            .SingleOrDefaultAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public Task<bool> HasUsersAsync(CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(cancellationToken);
}

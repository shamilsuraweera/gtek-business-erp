using Erp.SharedKernel;

namespace Erp.Modules.Platform.Domain;

public sealed class Company : AggregateRoot<CompanyId>
{
    private Company(CompanyId id, string name) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Company name is required.");
        }

        Name = name.Trim();
    }

    public string Name { get; private set; }

    public static Company Create(string name) => new(CompanyId.New(), name);
}

public sealed record User(UserId Id, CompanyId CompanyId, string DisplayName);

public sealed record Role(Guid Id, CompanyId CompanyId, string Name);

public sealed record Permission(Guid Id, string Code);

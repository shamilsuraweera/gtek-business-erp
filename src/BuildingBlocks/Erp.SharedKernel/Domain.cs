namespace Erp.SharedKernel;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public abstract class Entity<TId>
{
    protected Entity()
    {
    }

    protected Entity(TId id) => Id = id;

    public TId Id { get; private set; } = default!;
}

public abstract class AggregateRoot<TId> : Entity<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(TId id) : base(id)
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyCollection<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}

public sealed class DomainException(string message) : Exception(message);

public readonly record struct CompanyId(Guid Value)
{
    public static CompanyId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct PermissionId(Guid Value)
{
    public static PermissionId New() => new(Guid.NewGuid());
}

public readonly record struct NumberSequenceId(Guid Value)
{
    public static NumberSequenceId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public sealed record AuditMetadata(
    DateTimeOffset CreatedAt,
    UserId CreatedBy,
    DateTimeOffset? ModifiedAt = null,
    UserId? ModifiedBy = null);

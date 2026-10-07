namespace Logistica.SharedKernel;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    //Guid v7: Ordenado por tiempo, asi los indices de PostgreSQL no se fragmentan.
    public Guid Id { get; private init; } = Guid.CreateVersion7();

    protected Entity() { }

    // Id fijo: sólo para los datos iniciales, que necesitan Ids conocidos de antemano.
    protected Entity(Guid id)
    {
        Id = id;
    }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

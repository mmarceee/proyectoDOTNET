namespace Logistica.SharedKernel;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    //Guid v7: Ordenado por tiempo, asi los indices de PostgreSQL no se fragmentan.
    public Guid Id { get; private init; } = Guid.CreateVersion7();

    protected Entity() { }

    // Id fijo: para los datos iniciales, que necesitan Ids conocidos de antemano, y para las operaciones de la PWA,
    // cuyo Id genera el dispositivo (ADR-0004, sección 2.2).
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

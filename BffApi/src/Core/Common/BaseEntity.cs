namespace FleetStream.Core.Common;

/// <summary>
/// Base class for all entities with common properties.
/// </summary>
public abstract class BaseEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Base class for entities with soft delete support.
/// </summary>
public abstract class SoftDeletableEntity : BaseEntity
{
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Represents a value object.
/// </summary>
public abstract class ValueObject<T> where T : ValueObject<T>
{
    public override bool Equals(object? obj)
    {
        if (obj is not T other) return false;
        if (ReferenceEquals(this, other)) return true;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
    }

    public static bool operator ==(ValueObject<T>? a, ValueObject<T>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    public static bool operator !=(ValueObject<T>? a, ValueObject<T>? b)
    {
        return !(a == b);
    }

    protected abstract IEnumerable<object?> GetEqualityComponents();
}

namespace Idora.EntityFrameworkCore.Tests.Support;

/// <summary>Registered as <c>usr</c>, keyed by a string configured with the registry overload.</summary>
public sealed class User
{
    public string? Id { get; set; }

    public required string Name { get; set; }

    public List<Order> Orders { get; } = [];
}

/// <summary>Registered as <c>ord</c>, keyed by a string configured without a registry.</summary>
public sealed class Order
{
    public string Id { get; set; } = null!;

    public string? UserId { get; set; }

    public User? User { get; set; }

    public decimal Total { get; set; }
}

/// <summary>Registered as <c>inv</c>, keyed and referenced by <see cref="IdoraId"/> properties.</summary>
public sealed class Invoice
{
    public IdoraId Id { get; set; }

    /// <summary>An optional self-reference, to cover a nullable <see cref="IdoraId"/> foreign key.</summary>
    public IdoraId? CorrectsId { get; set; }

    /// <summary>A plain, non-key <see cref="IdoraId"/> column.</summary>
    public IdoraId RaisedBy { get; set; }

    /// <summary>A generated string column that is not the key.</summary>
    public string? PublicReference { get; set; }
}

/// <summary>The root of a hierarchy, registered as <c>ani</c>.</summary>
public abstract class Animal
{
    public string? Id { get; set; }
}

/// <summary>Not registered: takes the root's prefix.</summary>
public sealed class Dog : Animal;

/// <summary>Registered as <c>cat</c> in its own right.</summary>
public sealed class Cat : Animal;

/// <summary>In the model, keyed by an Idora key, but never registered.</summary>
public sealed class Note
{
    public string? Id { get; set; }
}

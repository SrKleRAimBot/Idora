namespace Idora.Tests.Support;

/// <summary>
/// Stand-in entity types. Nothing is required of an entity beyond being a type, so these are empty
/// on purpose.
/// </summary>
internal sealed class User;

internal sealed class Order;

internal sealed class Invoice;

internal sealed class Account;

/// <summary>Used to prove that a type with no registration is rejected rather than guessed at.</summary>
internal sealed class UnregisteredEntity;

/// <summary>A struct entity, to prove value types work as registry keys too.</summary>
internal readonly struct Payment;

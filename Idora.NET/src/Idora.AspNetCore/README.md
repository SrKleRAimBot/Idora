# Idora.NET.AspNetCore

ASP.NET Core support for [Idora.NET](https://github.com/SrKleRAimBot/Idora/tree/main/Idora.NET)
identifiers: a route constraint that matches only the right entity's identifiers, and validation
attributes that check a request carries them.

```csharp
app.MapGet("/users/{id:idora(usr)}", (IdoraId id) => ...);   // /users/ord_01k2… is a 404
```

> Part of [**Idora**](https://github.com/SrKleRAimBot/Idora): the same identifiers, with the same
> rules, for .NET, JavaScript/TypeScript, Java, Go and Rust.

---

## Install

```bash
dotnet add package Idora.NET.AspNetCore
```

Targets `net8.0`, `net9.0`, `net10.0` and `net11.0`, and depends on `Idora.NET` and the ASP.NET Core shared
framework. Trim- and AOT-safe.

### You may not need it

`IdoraId` binds on its own, from the core package alone. It implements `IParsable<T>` and carries a
`TypeConverter` and a JSON converter, so with nothing registered:

```csharp
app.MapGet("/orders/{id}", (IdoraId id) => ...);                  // route
app.MapGet("/orders", (IdoraId? after) => ...);                   // query string
app.MapPost("/orders", (PlaceOrder order) => ...);                // JSON body with IdoraId properties

public sealed class OrdersController : ControllerBase
{
    [HttpGet("{id}")] public IActionResult Get(IdoraId id) => ...; // MVC
}
```

Malformed input is a **400**: minimal APIs reject it during binding, and an `[ApiController]`
returns a `ValidationProblemDetails` naming the parameter. This package adds the two things binding
cannot do — choose between routes, and check a `string` property.

---

## The `idora` route constraint

```csharp
builder.Services.AddIdoraRouteConstraint();
```

| Template | Matches |
| --- | --- |
| `{id:idora}` | any well-formed identifier, of any entity |
| `{id:idora(usr)}` | only identifiers whose prefix is exactly `usr`, case included |

A route constraint decides whether a route **matches**, not whether a request is valid. An
identifier that fails it makes the route not match — a 404, or another route that does match.
That is the right answer for `/users/ord_01k2…`, which names no user, and it lets routes that
differ only in the kind of identifier sit side by side:

```csharp
app.MapGet("/lookup/{id:idora(usr)}", (IdoraId id) => GetUser(id));
app.MapGet("/lookup/{id:idora(ord)}", (IdoraId id) => GetOrder(id));
```

If you would rather answer a malformed identifier with a 400, bind it as an `IdoraId` without the
constraint and let binding reject it.

The constraint works the same in minimal APIs, attribute-routed controllers and Razor Pages, and in
link generation: `LinkGenerator` will not build `/users/{id:idora(usr)}` from an `ord` identifier.
A template with a prefix that could never appear in an identifier, such as `{id:idora(us_r)}`,
fails as soon as routing builds its tables — on the first request — with an
`InvalidEntityPrefixException` naming it, instead of silently matching nothing. The check itself
allocates nothing.

---

## Validation attributes

For `string` properties — or `IdoraId` ones whose prefix matters — on request models:

```csharp
public sealed class PlaceOrder
{
    [Required, IdoraIdFor<Customer>]     // the prefix registered for Customer
    public string? CustomerId { get; init; }

    [IdoraId("cpn")]                     // an explicit prefix
    public string? CouponId { get; init; }

    [IdoraId]                            // any well-formed identifier
    public string? ReferrerId { get; init; }
}
```

| Attribute | Requires |
| --- | --- |
| `[IdoraId]` | a well-formed identifier |
| `[IdoraId("usr")]` | a well-formed identifier with exactly that prefix |
| `[IdoraIdFor<User>]` | a well-formed identifier with the prefix registered for `User` |
| `[IdoraIdFor(typeof(User))]` | the same, for code that cannot use generic attributes |

They work wherever DataAnnotations do: MVC and Razor Pages model validation (an `[ApiController]`
answers with a `ValidationProblemDetails`), options validation, `Validator`, and any other
validator that runs `ValidationAttribute`s. Messages name the field and what was expected:

```
The CustomerId field must be an identifier for a Customer.
The CouponId field must be an identifier with the prefix 'cpn', such as cpn_01k2f9v8ta7m3x0qbrn4dzeh5c.
```

Set `ErrorMessage` to change them; `{0}` is the field, and `{1}` the prefix (`[IdoraId]`) or the
entity type's name (`[IdoraIdFor]`), with `{2}` its registered prefix.

A few details worth knowing:

- **`null` passes**, as it does for every validation attribute. Add `[Required]` to insist on a
  value.
- **`default(IdoraId)` fails.** On a non-nullable `IdoraId` property it is what a missing value looks
  like.
- **`[IdoraIdFor]` needs the request's services** to find the registry that `AddIdora` registers.
  MVC and Razor Pages supply them; with `Validator`, create the
  `ValidationContext` with a service provider. Without one it says so, rather than passing quietly.
- **An unregistered entity type throws** `EntityPrefixNotRegisteredException` instead of failing
  validation: the configuration is wrong, not the request.
- **On a positional record, MVC reads validation attributes from the constructor parameters.**
  Write `record PlaceOrder([Required, IdoraIdFor<Customer>] string CustomerId)`, without a
  `property:` target, or MVC refuses the model.

## License

MIT — see [LICENSE](https://github.com/SrKleRAimBot/Idora/blob/main/LICENSE).

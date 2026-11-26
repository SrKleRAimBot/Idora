using Microsoft.Extensions.DependencyInjection;

namespace Idora.EntityFrameworkCore.Tests.Support;

/// <summary>Builds a prefix registry the way an application gets one, for model-building tests.</summary>
public static class Registries
{
    public static IEntityPrefixRegistry Create(Action<IdoraOptions> configure) =>
        new ServiceCollection()
            .AddIdora(configure)
            .BuildServiceProvider()
            .GetRequiredService<IEntityPrefixRegistry>();
}

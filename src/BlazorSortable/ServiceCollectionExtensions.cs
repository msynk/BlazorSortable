using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorSortable;

/// <summary>
/// Registration helpers for the BlazorSortable component.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="BlazorSortableService"/> required by <see cref="BlazorSortable{TItem}"/>.
    /// Scoped so each Blazor Server circuit gets an isolated drag state. Calling it more
    /// than once is harmless.
    /// </summary>
    public static IServiceCollection AddBlazorSortable(this IServiceCollection services)
    {
        services.TryAddScoped<BlazorSortableService>();
        return services;
    }
}

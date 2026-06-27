using Microsoft.Extensions.DependencyInjection;

namespace BlazorSortable;

/// <summary>
/// Registration helpers for the BlazorSortable component.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="BlazorSortableService"/> required by <see cref="BlazorSortable{TItem}"/>.
    /// Scoped so each Blazor Server circuit gets an isolated drag state.
    /// </summary>
    public static IServiceCollection AddBlazorSortable(this IServiceCollection services)
    {
        services.AddScoped<BlazorSortableService>();
        return services;
    }
}

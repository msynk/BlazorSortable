using Microsoft.Extensions.DependencyInjection;

namespace BlazorSortable;

/// <summary>
/// Registration helpers for the BlazorSortable component.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="SortableService"/> required by <see cref="Sortable{TItem}"/>.
    /// Scoped so each Blazor Server circuit gets an isolated drag state.
    /// </summary>
    public static IServiceCollection AddBlazorSortable(this IServiceCollection services)
    {
        services.AddScoped<SortableService>();
        return services;
    }
}

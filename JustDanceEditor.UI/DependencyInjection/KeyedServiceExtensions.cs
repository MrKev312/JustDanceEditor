using Microsoft.Extensions.DependencyInjection;

namespace JustDanceEditor.UI.DependencyInjection;

public sealed record KeyedServiceDescriptor<T>(string Key, Func<IServiceProvider, T> Factory);

public static class KeyedServiceExtensions
{
    public static IServiceCollection AddKeyedSingleton<TService, TImplementation>(this IServiceCollection services, string key)
        where TImplementation : class, TService
        where TService : class
    {
        services.AddSingleton<TImplementation>();
        // Also make the implementation available via the service type so IEnumerable<IJdiFormat> can enumerate all formats
        services.AddSingleton<TService>(sp => sp.GetRequiredService<TImplementation>());
        services.AddSingleton(new KeyedServiceDescriptor<TService>(key, sp => sp.GetRequiredService<TImplementation>()));
        services.AddSingleton<IKeyedServiceProvider<TService>, KeyedServiceProvider<TService>>();
        return services;
    }
}

public interface IKeyedServiceProvider<T>
{
    T Get(string key);
    bool TryGet(string key, out T? value);
}

internal sealed class KeyedServiceProvider<T>(IServiceProvider sp, IEnumerable<KeyedServiceDescriptor<T>> descriptors) : IKeyedServiceProvider<T>
{
    private readonly IServiceProvider _sp = sp;
    private readonly IDictionary<string, Func<IServiceProvider, T>> _map = descriptors.ToDictionary(d => d.Key, d => d.Factory, StringComparer.OrdinalIgnoreCase);

    public T Get(string key)
    {
        if (!_map.TryGetValue(key, out Func<IServiceProvider, T>? factory))
            throw new KeyNotFoundException($"No keyed service registered for key '{key}'.");
        return factory(_sp);
    }

    public bool TryGet(string key, out T? value)
    {
        if (_map.TryGetValue(key, out Func<IServiceProvider, T>? factory))
        {
            value = factory(_sp);
            return true;
        }

        value = default;
        return false;
    }
}
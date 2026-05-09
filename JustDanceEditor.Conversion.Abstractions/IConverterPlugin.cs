using Microsoft.Extensions.DependencyInjection;

namespace JustDanceEditor.Conversion.Abstractions;

public interface IConverterPlugin
{
    string Code { get; }
    string DisplayName { get; }
    int Priority => 100;
    void ConfigureServices(IServiceCollection services);
}

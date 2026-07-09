using Microsoft.Extensions.DependencyInjection;

using System;

namespace JustDanceEditor.Editor.Services;

internal sealed class EditorObjectFactory(IServiceProvider services) : IEditorObjectFactory
{
    public object Create(Type type) => ActivatorUtilities.CreateInstance(services, type);
    public T Create<T>() where T : notnull => ActivatorUtilities.CreateInstance<T>(services);
}

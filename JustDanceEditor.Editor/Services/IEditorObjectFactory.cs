using System;

namespace JustDanceEditor.Editor.Services;

public interface IEditorObjectFactory
{
    object Create(Type type);
    T Create<T>() where T : notnull;
}
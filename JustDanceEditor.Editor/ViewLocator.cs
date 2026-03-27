using Avalonia.Controls;
using Avalonia.Controls.Templates;

using Dock.Model.Core;

using JustDanceEditor.Editor.ViewModels;

using System;

namespace JustDanceEditor.Editor;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        string fullName = param.GetType().FullName ?? throw new InvalidOperationException("View model type does not have a full name.");
        string name = fullName.Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? type = Type.GetType(name) ?? throw new InvalidOperationException("Missing view for " + name);
        object? instance = Activator.CreateInstance(type);
        return instance as Control ?? throw new InvalidOperationException($"View type '{name}' is not a control or could not be created.");
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase or IDockable;
    }
}
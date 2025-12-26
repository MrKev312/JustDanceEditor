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

        string name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? type = Type.GetType(name);

        return type != null 
            ? (Control)Activator.CreateInstance(type)! 
            : new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase or IDockable;
    }
}
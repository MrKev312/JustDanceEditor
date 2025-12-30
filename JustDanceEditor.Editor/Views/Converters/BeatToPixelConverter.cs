// File: .\Views\Converters\BeatToPixelConverter.cs
using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Data.Converters;

namespace JustDanceEditor.Editor.Views.Converters;

public class BeatToPixelConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 &&
            values[0] is double beat &&
            values[1] is double pixelsPerBeat)
        {
            double offset = values.Count >= 3 && values[2] is int o ? o : 0;
            // Return index position (label - offset) * ppb
            return (beat - offset) * pixelsPerBeat;
        }

        return 0.0;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JustDanceEditor.Converter.Converters;

public interface IUbiArtToUnityConverter
{
    Task Convert(ConversionRequest conversionRequest);
}

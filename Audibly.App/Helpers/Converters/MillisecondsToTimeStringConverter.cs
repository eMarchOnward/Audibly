using System;
using Audibly.App.Extensions;
using Microsoft.UI.Xaml.Data;

namespace Audibly.App.Helpers.Converters;

public class MillisecondsToTimeStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is long ms)
        {
            return ms.ToStr_ms();
        }
        return "0:00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
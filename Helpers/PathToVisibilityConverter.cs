using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

// Название класса конвертера
namespace Helinstaller.Helpers
{ 
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Проверяем, является ли входное значение строкой и не является ли оно пустым/null
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            // Путь существует/не пуст, делаем Card видимым
            return Visibility.Visible;
        }

        // Путь пуст или null, делаем Card скрытым
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Одностороннее преобразование, не используется
        throw new NotImplementedException();
    }
}}
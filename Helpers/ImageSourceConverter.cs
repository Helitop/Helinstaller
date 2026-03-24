using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Helinstaller.Helpers
{
        public class StringToImageSourceConverter : IValueConverter
        {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? path = value as string;
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return new BitmapImage(new Uri(path));

                // 1. ОЧИСТКА ПУТИ: убираем любые начальные слэши
                // Чтобы "/Assets/img.png" превратилось в "Assets/img.png"
                string cleanPath = path.TrimStart('/', '\\');

                // 2. Сборка ПОЛНОГО пути относительно EXE
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cleanPath);

                if (!File.Exists(fullPath))
                {
                    // Если не нашли, попробуем поискать просто в папке Assets (на всякий случай)
                    string fileName = Path.GetFileName(path);
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);

                    if (!File.Exists(fullPath))
                    {
                        Debug.WriteLine($"!!! IMAGE NOT FOUND AT: {fullPath}");
                        return null;
                    }
                }

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!! IMAGE ERROR: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
        }
    }

using System;
using System.Windows;

namespace Helinstaller.Helpers
{
    public enum ToastType
    {
        Success, // Зеленая галочка
        Warning, // Желтый восклицательный знак
        Error,   // Красный крестик
        Info     // Синяя инфо-иконка
    }

    public static class CapsuleToastService
    {
        public static event Action<string, ToastType>? OnShowToast;

        public static void Show(string message, ToastType type = ToastType.Success)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                OnShowToast?.Invoke(message, type);
            });
        }
    }
}
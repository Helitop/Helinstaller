using System;
using System.Runtime.InteropServices;
using Windows.Media;

namespace Helinstaller.Helpers
{
    [ComImport]
    [Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    public interface ISystemMediaTransportControlsInterop
    {
        SystemMediaTransportControls GetForWindow(IntPtr appWindow, [In] ref Guid riid);
    }

    public static class SmtcInteropHelper
    {
        [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int RoGetActivationFactory(
            [MarshalAs(UnmanagedType.HString)] string activatableClassId,
            [In] ref Guid iid,
            out IntPtr factory);

        public static SystemMediaTransportControls GetForWindow(IntPtr hwnd)
        {
            try
            {
                // Попытка использовать стандартный встроенный .NET SDK Interop
                return SystemMediaTransportControlsInterop.GetForWindow(hwnd);
            }
            catch
            {
                // Резервный ручной COM-вызов, если версия SDK не сгенерировала статический метод
                Guid interopGuid = new Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
                Guid smtcGuid = typeof(SystemMediaTransportControls).GUID;

                int hr = RoGetActivationFactory("Windows.Media.SystemMediaTransportControls", ref interopGuid, out IntPtr factoryPtr);
                if (hr != 0) throw Marshal.GetExceptionForHR(hr);

                var factory = (ISystemMediaTransportControlsInterop)Marshal.GetUniqueObjectForIUnknown(factoryPtr);
                return factory.GetForWindow(hwnd, ref smtcGuid);
            }
        }
    }
}
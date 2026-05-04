using System.Runtime.InteropServices;

namespace MSLX.Plugin.RustBridge;

internal static class RustInterop
{
    private const string LIB = "mslx_plugin_rustbridge";

    [DllImport(LIB, EntryPoint = "plugin_init", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Init(
        LogCallback logInfo,
        LogCallback logWarn,
        LogCallback logError,
        LogErrorCallback logErrorEx,
        LogCallback logDebug,
        SdkCallCallback sdkCall
    );

    [DllImport(LIB, EntryPoint = "plugin_unload", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Unload();

    [DllImport(LIB, EntryPoint = "plugin_handle_request", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int HandleRequest(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string requestJson,
        out IntPtr responsePtr,
        out nuint responseLen
    );

    [DllImport(LIB, EntryPoint = "plugin_free_response", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeResponse(IntPtr ptr, nuint len);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void LogCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void LogErrorCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? exMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.LPUTF8Str)]
    internal delegate string SdkCallCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string method,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string argsJson);
}

using System.Reflection;
using System.Runtime.InteropServices;

namespace MSLX.Plugin.RustBridge;

public sealed class RustNativeLoader : IDisposable
{
    private readonly PluginInitDelegate _init;
    private readonly PluginUnloadDelegate _unload;
    private readonly PluginHandleRequestDelegate _handleRequest;
    private readonly PluginFreeResponseDelegate _freeResponse;
    private IntPtr _handle;

    public RustNativeLoader(string libraryName)
    {
        LibraryName = libraryName;
        _handle = LoadNativeLibrary(libraryName);
        _init = LoadExport<PluginInitDelegate>("plugin_init");
        _unload = LoadExport<PluginUnloadDelegate>("plugin_unload");
        _handleRequest = LoadExport<PluginHandleRequestDelegate>("plugin_handle_request");
        _freeResponse = LoadExport<PluginFreeResponseDelegate>("plugin_free_response");
    }

    public string LibraryName { get; }

    public int Init(
        LogCallback logInfo,
        LogCallback logWarn,
        LogCallback logError,
        LogErrorCallback logErrorEx,
        LogCallback logDebug,
        SdkCallCallback sdkCall)
        => _init(logInfo, logWarn, logError, logErrorEx, logDebug, sdkCall);

    public void Unload() => _unload();

    public int HandleRequest(string requestJson, out IntPtr responsePtr, out nuint responseLen)
        => _handleRequest(requestJson, out responsePtr, out responseLen);

    public void FreeResponse(IntPtr ptr, nuint len) => _freeResponse(ptr, len);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        NativeLibrary.Free(_handle);
        _handle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private T LoadExport<T>(string exportName) where T : Delegate
    {
        var export = NativeLibrary.GetExport(_handle, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(export);
    }

    private static IntPtr LoadNativeLibrary(string libraryName)
    {
        if (NativeLibrary.TryLoad(libraryName, out var handle))
            return handle;

        var platformFileName = GetPlatformFileName(libraryName);
        foreach (var path in GetLibrarySearchPaths(platformFileName))
            if (NativeLibrary.TryLoad(path, out handle))
                return handle;

        throw new DllNotFoundException(
            $"Unable to load Rust native library '{libraryName}' ({platformFileName}).");
    }

    private static IEnumerable<string> GetLibrarySearchPaths(string platformFileName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, platformFileName);

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
            yield return Path.Combine(assemblyDirectory, platformFileName);
    }

    private static string GetPlatformFileName(string libraryName)
    {
        if (OperatingSystem.IsWindows())
            return libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? libraryName
                : $"{libraryName}.dll";

        if (OperatingSystem.IsMacOS())
            return libraryName.StartsWith("lib", StringComparison.Ordinal)
                ? $"{libraryName}.dylib"
                : $"lib{libraryName}.dylib";

        return libraryName.StartsWith("lib", StringComparison.Ordinal)
            ? $"{libraryName}.so"
            : $"lib{libraryName}.so";
    }

    ~RustNativeLoader() => Dispose();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogErrorCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? exMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.LPUTF8Str)]
    public delegate string SdkCallCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string method,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string argsJson);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PluginInitDelegate(
        LogCallback logInfo,
        LogCallback logWarn,
        LogCallback logError,
        LogErrorCallback logErrorEx,
        LogCallback logDebug,
        SdkCallCallback sdkCall);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PluginUnloadDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PluginHandleRequestDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string requestJson,
        out IntPtr responsePtr,
        out nuint responseLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PluginFreeResponseDelegate(IntPtr ptr, nuint len);
}

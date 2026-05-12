using System.Reflection;
using System.Runtime.InteropServices;

namespace MSLX.Plugin.RustBridge;

public sealed class RustNativeLoader : IDisposable
{
    private const string EmbeddedResourcePrefix = "RustBridge.Native";

    private readonly PluginInitDelegate _init;
    private readonly PluginUnloadDelegate _unload;
    private readonly PluginHandleRequestDelegate _handleRequest;
    private readonly PluginFreeResponseDelegate _freeResponse;
    private IntPtr _handle;

    public RustNativeLoader(string libraryName, Assembly? resourceAssembly = null)
    {
        LibraryName = libraryName;
        _handle = LoadNativeLibrary(libraryName, resourceAssembly);
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

    private static IntPtr LoadNativeLibrary(string libraryName, Assembly? resourceAssembly)
    {
        var platformFileName = GetPlatformFileName(libraryName);

        if (TryLoadEmbeddedNativeLibrary(platformFileName, resourceAssembly, out var handle))
            return handle;

        if (NativeLibrary.TryLoad(libraryName, out handle))
            return handle;

        foreach (var path in GetLibrarySearchPaths(platformFileName))
            if (NativeLibrary.TryLoad(path, out handle))
                return handle;

        throw new DllNotFoundException(
            $"Unable to load Rust native library '{libraryName}' ({platformFileName}).");
    }

    private static bool TryLoadEmbeddedNativeLibrary(
        string platformFileName,
        Assembly? resourceAssembly,
        out IntPtr handle)
    {
        foreach (var assembly in GetResourceAssemblies(resourceAssembly))
        {
            var resourceName = FindNativeResource(assembly, platformFileName);
            if (resourceName is null)
                continue;

            var extractedPath = ExtractNativeResource(assembly, resourceName, platformFileName);
            if (NativeLibrary.TryLoad(extractedPath, out handle))
                return true;
        }

        handle = IntPtr.Zero;
        return false;
    }

    private static IEnumerable<Assembly> GetResourceAssemblies(Assembly? preferredAssembly)
    {
        var seen = new HashSet<Assembly>();

        if (preferredAssembly is not null && seen.Add(preferredAssembly))
            yield return preferredAssembly;

        var executingAssembly = Assembly.GetExecutingAssembly();
        if (seen.Add(executingAssembly))
            yield return executingAssembly;

        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null && seen.Add(entryAssembly))
            yield return entryAssembly;
    }

    private static string? FindNativeResource(Assembly assembly, string platformFileName)
    {
        var resourceNames = assembly.GetManifestResourceNames();
        var runtimeIdentifier = GetRuntimeIdentifier();
        var runtimeSuffix = $".{runtimeIdentifier}.{platformFileName}";
        var simpleSuffix = $".{EmbeddedResourcePrefix}.{platformFileName}";

        return resourceNames.FirstOrDefault(name =>
            name.Equals($"{EmbeddedResourcePrefix}.{runtimeIdentifier}.{platformFileName}", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(runtimeSuffix, StringComparison.OrdinalIgnoreCase))
            ?? resourceNames.FirstOrDefault(name =>
                name.Equals($"{EmbeddedResourcePrefix}.{platformFileName}", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(simpleSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractNativeResource(
        Assembly assembly,
        string resourceName,
        string platformFileName)
    {
        using var resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new DllNotFoundException($"Embedded native resource '{resourceName}' was not found.");

        var targetDirectory = Path.Combine(
            GetNativeCacheRoot(),
            SanitizePathPart(assembly.GetName().Name ?? "plugin"),
            assembly.ManifestModule.ModuleVersionId.ToString("N"),
            GetRuntimeIdentifier());
        Directory.CreateDirectory(targetDirectory);

        var targetPath = Path.Combine(targetDirectory, platformFileName);
        if (File.Exists(targetPath))
            return targetPath;

        var tempPath = Path.Combine(targetDirectory, $"{platformFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                resourceStream.CopyTo(output);

            File.Move(tempPath, targetPath);
        }
        catch (IOException) when (File.Exists(targetPath))
        {
            File.Delete(tempPath);
        }

        return targetPath;
    }

    private static string GetNativeCacheRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;

        return Path.Combine(root, "MSLX.Plugin.RustBridge", "native");
    }

    private static string SanitizePathPart(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');

        return value;
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

    private static string GetRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{arch}";
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

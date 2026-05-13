using System.Reflection;
using System.Runtime.InteropServices;

namespace MSLX.Plugin.RustBridge;

public sealed class RustNativeLoader : IDisposable
{
    private const string EmbeddedResourcePrefix = "RustBridge.Native";
    private static readonly Lazy<bool> IsMuslLinuxRuntime = new(DetectMuslLinux);

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

            var extractedPath = ExtractNativeResource(
                assembly,
                resourceName.Value.Name,
                resourceName.Value.RuntimeIdentifier,
                platformFileName);
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

    private static NativeResource? FindNativeResource(Assembly assembly, string platformFileName)
    {
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var runtimeIdentifier in GetRuntimeIdentifierCandidates())
        {
            var exactName = $"{EmbeddedResourcePrefix}.{runtimeIdentifier}.{platformFileName}";
            var runtimeSuffix = $".{runtimeIdentifier}.{platformFileName}";
            var resourceName = resourceNames.FirstOrDefault(name =>
                name.Equals(exactName, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(runtimeSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName is not null)
                return new NativeResource(resourceName, runtimeIdentifier);
        }

        var simpleName = $"{EmbeddedResourcePrefix}.{platformFileName}";
        var simpleSuffix = $".{EmbeddedResourcePrefix}.{platformFileName}";
        var simpleResourceName = resourceNames.FirstOrDefault(name =>
            name.Equals(simpleName, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(simpleSuffix, StringComparison.OrdinalIgnoreCase));

        return simpleResourceName is null
            ? null
            : new NativeResource(simpleResourceName, GetPortableRuntimeIdentifier());
    }

    private static string ExtractNativeResource(
        Assembly assembly,
        string resourceName,
        string runtimeIdentifier,
        string platformFileName)
    {
        using var resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new DllNotFoundException($"Embedded native resource '{resourceName}' was not found.");

        var targetDirectory = Path.Combine(
            GetNativeCacheRoot(),
            SanitizePathPart(assembly.GetName().Name ?? "plugin"),
            assembly.ManifestModule.ModuleVersionId.ToString("N"),
            runtimeIdentifier);
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

    private static IEnumerable<string> GetRuntimeIdentifierCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactRuntimeIdentifier = RuntimeInformation.RuntimeIdentifier;

        foreach (var runtimeIdentifier in GetOrderedRuntimeIdentifierCandidates(exactRuntimeIdentifier))
            if (seen.Add(runtimeIdentifier))
                yield return runtimeIdentifier;

        var portableRuntimeIdentifier = GetPortableRuntimeIdentifier();
        foreach (var runtimeIdentifier in GetOrderedRuntimeIdentifierCandidates(portableRuntimeIdentifier))
            if (seen.Add(runtimeIdentifier))
                yield return runtimeIdentifier;
    }

    private static IEnumerable<string> GetOrderedRuntimeIdentifierCandidates(string runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            yield break;

        var aliases = GetRuntimeIdentifierAliases(runtimeIdentifier).ToArray();
        if (ShouldPreferRuntimeIdentifierAliases(runtimeIdentifier))
            foreach (var alias in aliases)
                yield return alias;

        yield return runtimeIdentifier;

        if (!ShouldPreferRuntimeIdentifierAliases(runtimeIdentifier))
            foreach (var alias in aliases)
                yield return alias;
    }

    private static IEnumerable<string> GetRuntimeIdentifierAliases(string runtimeIdentifier)
    {
        var architecture = GetRuntimeIdentifierArchitecture(runtimeIdentifier);
        if (architecture is null)
            yield break;

        if (runtimeIdentifier.StartsWith("linux-musl-", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"alpine-{architecture}";
            yield break;
        }

        if (runtimeIdentifier.StartsWith("alpine-", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"linux-musl-{architecture}";
            yield break;
        }

        if (runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase) && IsMuslLinuxRuntime.Value)
        {
            yield return $"linux-musl-{architecture}";
            yield return $"alpine-{architecture}";
        }
    }

    private static bool ShouldPreferRuntimeIdentifierAliases(string runtimeIdentifier)
        => runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase)
           && !runtimeIdentifier.StartsWith("linux-musl-", StringComparison.OrdinalIgnoreCase)
           && IsMuslLinuxRuntime.Value;

    private static string? GetRuntimeIdentifierArchitecture(string runtimeIdentifier)
    {
        var separatorIndex = runtimeIdentifier.LastIndexOf('-');
        return separatorIndex < 0 || separatorIndex == runtimeIdentifier.Length - 1
            ? null
            : runtimeIdentifier[(separatorIndex + 1)..];
    }

    private static string GetPortableRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : OperatingSystem.IsFreeBSD()
                    ? "freebsd"
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

    private static bool DetectMuslLinux()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        if (File.Exists("/etc/alpine-release"))
            return true;

        foreach (var directory in new[] { "/lib", "/usr/lib" })
        {
            try
            {
                if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "ld-musl-*.so.1").Any())
                    return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    private readonly record struct NativeResource(string Name, string RuntimeIdentifier);

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

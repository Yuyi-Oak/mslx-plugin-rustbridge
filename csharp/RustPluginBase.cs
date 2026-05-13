using System.Reflection;
using System.Runtime.ExceptionServices;
using Newtonsoft.Json.Linq;
using SDK = MSLX.SDK;

namespace MSLX.Plugin.RustBridge;

public abstract class RustPluginBase : SDK.IPlugin, IDisposable
{
    private const string PluginConfigApiMissing =
        "MSLX SDK plugin config API requires MSLX.SDK 1.4.3 or newer.";
    private static readonly Lazy<MethodInfo?> PluginConfigMethod = new(FindPluginConfigMethod);

    private RustNativeLoader? _native;
    private RustNativeLoader.LogCallback? _logInfo;
    private RustNativeLoader.LogCallback? _logWarn;
    private RustNativeLoader.LogCallback? _logError;
    private RustNativeLoader.LogCallback? _logDebug;
    private RustNativeLoader.LogErrorCallback? _logErrorEx;
    private RustNativeLoader.SdkCallCallback? _sdkCall;

    public abstract string Id { get; }
    public virtual string Name => "MSLX RustBridge Plugin";
    public virtual string Description => "由 Rust 实现的 MSLX Plugin";
    public virtual string Icon => "https://www.mslmc.cn/logo.png";
    public virtual string Version => "1.2.0";
    public virtual string MinSDKVersion => "1.4.0";
    public virtual string Developer => "Yuyi-Oak";
    public virtual string AuthorUrl => "https://github.com/Yuyi-Oak";
    public virtual string PluginUrl => "https://github.com/Yuyi-Oak/mslx-plugin-rustbridge";

    protected virtual string RustLibraryName => "mslx_plugin_rustbridge";

    public virtual void OnLoad()
    {
        _logInfo = msg => SDK.MSLX.Logger.Info($"[{Id}] {msg}");
        _logWarn = msg => SDK.MSLX.Logger.Warn($"[{Id}] {msg}");
        _logError = msg => SDK.MSLX.Logger.Error($"[{Id}] {msg}");
        _logErrorEx = (msg, ex) => SDK.MSLX.Logger.Error(
            $"[{Id}] {msg}",
            ex is null ? null : new Exception(ex));
        _logDebug = msg => SDK.MSLX.Logger.Debug($"[{Id}] {msg}");
        _sdkCall = HandleSdkCall;

        _native = new RustNativeLoader(RustLibraryName, GetType().Assembly);
        int result = _native.Init(
            _logInfo, _logWarn, _logError, _logErrorEx, _logDebug, _sdkCall);

        if (result != 0)
        {
            SDK.MSLX.Logger.Error($"[{Id}] RustBridge plugin_init 返回错误码: {result}");
            return;
        }

        SDK.MSLX.Logger.Info($"[{Id}] RustBridge 插件加载成功。");
        OnRustLoaded();
    }

    public virtual void OnUnload()
    {
        try
        {
            OnRustUnloading();
            _native?.Unload();
            SDK.MSLX.Logger.Info($"[{Id}] RustBridge 插件卸载。");
        }
        finally
        {
            Dispose();
        }
    }

    public int HandleRequest(string requestJson, out IntPtr responsePtr, out nuint responseLen)
    {
        responsePtr = IntPtr.Zero;
        responseLen = 0;
        return _native?.HandleRequest(requestJson, out responsePtr, out responseLen) ?? -100;
    }

    public void FreeResponse(IntPtr ptr, nuint len) => _native?.FreeResponse(ptr, len);

    public void Dispose()
    {
        _native?.Dispose();
        _native = null;
        GC.SuppressFinalize(this);
    }

    protected virtual void OnRustLoaded() {}

    protected virtual void OnRustUnloading() {}

    protected virtual string HandleSdkCall(string method, string argsJson)
    {
        try
        {
            var args = JObject.Parse(argsJson);
            return method switch
            {
                "config.main.read"
                    => SDK.MSLX.Config.Main.ReadConfig().ToString(),

                "config.main.read_key"
                    => (SDK.MSLX.Config.Main.ReadConfigKey(args["key"]!.ToString())
                        ?? JValue.CreateNull()).ToString(),

                "config.main.write_key"
                    => Exec(() => SDK.MSLX.Config.Main.WriteConfigKey(
                        args["key"]!.ToString(), args["value"]!)),

                "plugin.config.get_data_path"
                    => JValue.CreateString(GetPluginConfigDataPath()).ToString(),

                "plugin.config.read"
                    => GetPluginConfig().ReadConfig().ToString(),

                "plugin.config.write"
                    => Exec(() => GetPluginConfig().WriteConfig(
                        args["content"]?.ToObject<JObject>()
                        ?? throw new Exception("content must be a JSON object"))),

                "plugin.config.read_key"
                    => (GetPluginConfig().ReadConfigKey(args["key"]!.ToString())
                        ?? JValue.CreateNull()).ToString(),

                "plugin.config.write_key"
                    => Exec(() => GetPluginConfig().WriteConfigKey(
                        args["key"]!.ToString(), args["value"]!)),

                "config.servers.get_list"
                    => JArray.FromObject(SDK.MSLX.Config.Servers.GetServerList()).ToString(),

                "config.servers.get_server"
                    => JObject.FromObject(
                        SDK.MSLX.Config.Servers.GetServer(args["id"]!.ToObject<uint>())
                        ?? throw new Exception("Server not found")).ToString(),

                "config.servers.create_server"
                    => BoolResult(SDK.MSLX.Config.Servers.CreateServer(
                        args["server"]!.ToObject<SDK.Models.McServerInfo.ServerInfo>()!)),

                "config.servers.delete_server"
                    => BoolResult(SDK.MSLX.Config.Servers.DeleteServer(
                        args["id"]!.ToObject<uint>(),
                        args["delete_files"]?.ToObject<bool>() ?? false)),

                "config.users.validate"
                    => BoolResult(SDK.MSLX.Config.Users.ValidateUser(
                        args["username"]!.ToString(), args["password"]!.ToString())),

                "config.users.get_by_api_key"
                    => NullableResult(SDK.MSLX.Config.Users.GetUserByApiKey(args["key"]!.ToString())),

                "config.users.get_by_username"
                    => NullableResult(SDK.MSLX.Config.Users.GetUserByUsername(args["username"]!.ToString())),

                "config.get_appdata_path"
                    => JValue.CreateString(SDK.MSLX.Config.GetAppDataPath()).ToString(),

                _ => Error($"Unknown SDK method: {method}")
            };
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static string Exec(Action action)
    {
        action();
        return "{\"ok\":true}";
    }

    private static string BoolResult(bool v) => $"{{\"result\":{(v ? "true" : "false")}}}";

    private static string NullableResult(object? v)
        => v is null ? "null" : JObject.FromObject(v).ToString();

    private static string Error(string msg)
        => new JObject { ["error"] = msg }.ToString();

    private string GetPluginConfigDataPath()
        => GetPluginConfig().GetDataPath();

    private PluginConfigBridge GetPluginConfig()
    {
        var method = PluginConfigMethod.Value
            ?? throw new NotSupportedException(PluginConfigApiMissing);

        var bridge = method.Invoke(null, new object[] { this })
            ?? throw new InvalidOperationException("MSLX SDK returned an empty plugin config bridge.");

        return new PluginConfigBridge(bridge);
    }

    private static MethodInfo? FindPluginConfigMethod()
        => typeof(SDK.IPlugin).Assembly
            .GetType("MSLX.SDK.PluginExtensions")
            ?.GetMethod(
                "Config",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(SDK.IPlugin) },
                null);

    private sealed class PluginConfigBridge
    {
        private readonly object _inner;

        public PluginConfigBridge(object inner)
        {
            _inner = inner;
        }

        public string GetDataPath()
            => (string)(Invoke(nameof(GetDataPath)) ?? string.Empty);

        public JObject ReadConfig()
            => (JObject)(Invoke(nameof(ReadConfig)) ?? new JObject());

        public void WriteConfig(JObject content)
            => Invoke(nameof(WriteConfig), content);

        public JToken? ReadConfigKey(string key)
            => Invoke(nameof(ReadConfigKey), key) as JToken;

        public void WriteConfigKey(string key, JToken value)
            => Invoke(nameof(WriteConfigKey), key, value);

        private object? Invoke(string methodName, params object?[] args)
        {
            var method = _inner.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMethodException(_inner.GetType().FullName, methodName);

            try
            {
                return method.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}

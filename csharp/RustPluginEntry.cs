using Newtonsoft.Json.Linq;
using SDK = MSLX.SDK;

namespace MSLX.Plugin.RustBridge;

public class RustPluginEntry : SDK.IPlugin
{
    public string Id            => "mslx-plugin-RustBridge";
    public string Name          => "MSLX RustBridge Plugin";
    public string Description   => "由 Rust 实现的 MSLX Plugin 从 C# 到 Rust 的转接层";
    public string Version       => "1.0.0";
    public string MinSDKVersion => "1.4.0";
    public string Developer     => "Yuyi-Oak";

    public void OnLoad()
    {
        _logInfo    = msg => SDK.MSLX.Logger.Info($"[{Id}] {msg}");
        _logWarn    = msg => SDK.MSLX.Logger.Warn($"[{Id}] {msg}");
        _logError   = msg => SDK.MSLX.Logger.Error($"[{Id}] {msg}");
        _logErrorEx = (msg, ex) => SDK.MSLX.Logger.Error($"[{Id}] {msg}",
                          ex is null ? null : new Exception(ex));
        _logDebug   = msg => SDK.MSLX.Logger.Debug($"[{Id}] {msg}");
        _sdkCall    = HandleSdkCall;

        int result = RustInterop.Init(
            _logInfo, _logWarn, _logError, _logErrorEx, _logDebug, _sdkCall);

        if (result != 0)
            SDK.MSLX.Logger.Error($"[{Id}] RustBridge plugin_init 返回错误码: {result}");
        else
            SDK.MSLX.Logger.Info($"[{Id}] RustBridge 插件加载成功。");
    }

    public void OnUnload()
    {
        RustInterop.Unload();
        SDK.MSLX.Logger.Info($"[{Id}] RustBridge 插件卸载。");
    }

    private string HandleSdkCall(string method, string argsJson)
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

    private static string Exec(Action action) { action(); return "{\"ok\":true}"; }
    private static string BoolResult(bool v) => $"{{\"result\":{(v ? "true" : "false")}}}";
    private static string NullableResult(object? v)
        => v is null ? "null" : JObject.FromObject(v).ToString();
    private static string Error(string msg)
        => new JObject { ["error"] = msg }.ToString();

    private RustInterop.LogCallback?      _logInfo, _logWarn, _logError, _logDebug;
    private RustInterop.LogErrorCallback? _logErrorEx;
    private RustInterop.SdkCallCallback?  _sdkCall;
}

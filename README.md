# MSLX Rust Plugin Bridge

MSLX 插件的 Rust 转接层模板。C# 做薄壳满足宿主要求，真正的业务逻辑全在 Rust 侧实现。

```
MSLX 宿主 (C#/.NET 10)
    │  加载 MSLX.Plugin.RustBridge.dll
    ▼
RustPluginEntry  ── IPlugin 实现，生命周期桥接
RustBridgeController  ── catch-all Controller，转发所有 HTTP 请求
    │  P/Invoke (FFI)
    ▼
libmslx_plugin_rustbridge.so / .dll
    ├── plugin_init          ← 接收 Logger + SdkCall 函数指针
    ├── plugin_handle_request ← 接收请求 JSON，返回响应 JSON
    ├── plugin_free_response  ← 释放 Rust 分配的内存
    └── plugin_unload
```

## 目录结构

```
mslx-plugin-rustbridge/
├── rust/
│   ├── Cargo.toml
│   └── src/
│       ├── lib.rs      — FFI 导出点
│       ├── bridge.rs   — 请求/响应数据结构
│       ├── sdk.rs      — MSLX.SDK 的 Rust 封装
│       └── router.rs   — 路由 & 业务逻辑（主要改这里）
├── csharp/
│   ├── MSLX.Plugin.RustBridge.csproj
│   ├── RustInterop.cs      — P/Invoke 声明
│   ├── RustPluginEntry.cs  — IPlugin 实现 + SDK 回调
│   └── RustBridgeController.cs — catch-all HTTP 控制器
├── build.sh   (Linux/macOS)
└── build.bat  (Windows)
```

## 快速开始

### 1. 修改插件元数据

在 `csharp/RustPluginEntry.cs` 里改：
```csharp
public string Id      => "mslx-plugin-你的插件id";
public string Name    => "你的插件名";
public string Version => "1.0.0";
```

在 `csharp/RustBridgeController.cs` 里把路由改成匹配的：
```csharp
[Route("api/plugins/mslx-plugin-你的插件id/{**subPath}")]
```

### 2. 把 MSLX.SDK.dll 放到 csharp/libs/

```
csharp/libs/MSLX.SDK.dll
```

### 3. 写业务逻辑

在 `rust/src/router.rs` 里添加路由和处理器：

```rust
pub fn dispatch(req: PluginRequest, sdk: &SdkBridge) -> PluginResponse {
    match (req.method.as_str(), req.sub_path.as_str()) {
        ("GET", "/hello") => PluginResponse::ok(json!({ "msg": "Hello!" })),
        // ...
        _ => PluginResponse::not_found(),
    }
}
```

调用 MSLX SDK：
```rust
// 读取服务器列表
let servers = sdk.config_servers_get_list()?;

// 写日志
sdk.info("Something happened");

// 验证用户
let ok = sdk.config_users_validate("admin", "password")?;
```

如需调用 `sdk.rs` 里还没封装的 SDK 方法，在 `SdkBridge` 里照样添加即可，
方法名对应 `csharp/RustPluginEntry.cs` 里 `HandleSdkCall` 的 switch 分支。

### 4. 构建

Linux/macOS：
```bash
chmod +x build.sh && ./build.sh
```

Windows：
```bat
build.bat
```

### 5. 部署

把 `csharp/bin/Release/net10.0/` 下的所有文件复制到 MSLX 插件目录：
- `MSLX.Plugin.RustBridge.dll`
- `libmslx_plugin_rustbridge.so` (Linux) 或 `mslx_plugin_rustbridge.dll` (Windows)

## 请求/响应格式

C# 传给 Rust 的 JSON：
```json
{
  "method":   "GET",
  "sub_path": "/servers/1",
  "query":    "?foo=bar",
  "headers":  { "Authorization": "Bearer xxx" },
  "body":     "{}"
}
```

Rust 返回给 C# 的 JSON：
```json
{
  "status":  200,
  "headers": { "X-Custom": "value" },
  "body":    { "id": 1, "name": "My Server" }
}
```

## SDK 调用表

| Rust 方法 | 对应 MSLX.SDK |
|-----------|--------------|
| `sdk.config_main_read()` | `MSLX.Config.Main.ReadConfig()` |
| `sdk.config_main_read_key(key)` | `MSLX.Config.Main.ReadConfigKey(key)` |
| `sdk.config_main_write_key(key, val)` | `MSLX.Config.Main.WriteConfigKey(key, val)` |
| `sdk.config_servers_get_list()` | `MSLX.Config.Servers.GetServerList()` |
| `sdk.config_servers_get_server(id)` | `MSLX.Config.Servers.GetServer(id)` |
| `sdk.config_servers_delete_server(id, del_files)` | `MSLX.Config.Servers.DeleteServer(id, del_files)` |
| `sdk.config_users_validate(user, pass)` | `MSLX.Config.Users.ValidateUser(user, pass)` |
| `sdk.config_users_get_by_api_key(key)` | `MSLX.Config.Users.GetUserByApiKey(key)` |
| `sdk.config_users_get_by_username(name)` | `MSLX.Config.Users.GetUserByUsername(name)` |
| `sdk.get_appdata_path()` | `MSLX.Config.GetAppDataPath()` |

需要其他 SDK 方法？在 `sdk.rs` 加 Rust 包装，在 `RustPluginEntry.cs` 的 switch 里加 C# 分支，两边对齐即可。

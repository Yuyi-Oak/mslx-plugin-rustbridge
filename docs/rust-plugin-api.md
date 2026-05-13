# Rust Plugin API

本文说明 Rust 动态库需要实现什么接口、C# 会传入什么数据、Rust 应该怎样返回响应，以及 SDK 回调应该怎样扩展。

## 基本约定

RustBridge 不会把 Rust 代码打进 NuGet 包。每个插件都应该维护自己的 Rust cdylib。示例工程会在构建时把当前平台原生库内嵌到插件 DLL，也可以按需要改成旁边文件部署。

默认库名是：

```text
mslx_plugin_rustbridge
```

不同平台的文件名：

```text
Linux:   libmslx_plugin_rustbridge.so
Windows: mslx_plugin_rustbridge.dll
macOS:   libmslx_plugin_rustbridge.dylib
```

内嵌到插件 DLL 时，资源名会带上 RID：

```text
RustBridge.Native.linux-x64.libmslx_plugin_rustbridge.so
RustBridge.Native.linux-arm64.libmslx_plugin_rustbridge.so
RustBridge.Native.linux-musl-x64.libmslx_plugin_rustbridge.so
RustBridge.Native.win-arm64.mslx_plugin_rustbridge.dll
RustBridge.Native.osx-arm64.libmslx_plugin_rustbridge.dylib
```

如果构建时显式指定 RID，RustBridge 会把常见 RID 转成 Rust target triple，例如 `linux-arm64` 对应 `aarch64-unknown-linux-gnu`，`osx-arm64` 对应 `aarch64-apple-darwin`。跨平台构建前需要先安装 Rust target，并准备好目标平台所需的链接器和系统库。

如果你改了库名，需要同时改三处：

- C# 插件入口里的 `RustLibraryName`。
- 插件 `.csproj` 里的 `RustBridgeRustLibName` 或对应内嵌/部署逻辑。
- Rust `Cargo.toml` 里的 `[lib] name`。

## 必须导出的函数

Rust 动态库必须导出下面四个符号。函数名必须完全一致。

```rust
#[unsafe(no_mangle)]
pub extern "C" fn plugin_init(
    log_info: LogFn,
    log_warn: LogFn,
    log_error: LogFn,
    log_error_ex: LogErrFn,
    log_debug: LogFn,
    sdk_call: SdkCallFn,
) -> i32;

#[unsafe(no_mangle)]
pub extern "C" fn plugin_handle_request(
    request_json: *const c_char,
    response_ptr: *mut *mut u8,
    response_len: *mut usize,
) -> i32;

#[unsafe(no_mangle)]
pub extern "C" fn plugin_free_response(ptr: *mut u8, len: usize);

#[unsafe(no_mangle)]
pub extern "C" fn plugin_unload();
```

示例代码位于：

```text
samples/RustBridgeDemo/rust/src/lib.rs
```

## 回调类型

示例中使用的类型如下：

```rust
pub type LogFn = extern "C" fn(*const c_char);
pub type LogErrFn = extern "C" fn(*const c_char, *const c_char);
pub type SdkCallFn = extern "C" fn(*const c_char, *const c_char) -> *const c_char;
```

这些回调由 C# 在 `RustPluginBase.OnLoad()` 中传入。

## 生命周期

加载插件时：

```text
MSLX 加载 C# 插件
  -> RustPluginBase.OnLoad()
    -> RustNativeLoader 加载原生库
      -> plugin_init(...)
```

卸载插件时：

```text
MSLX 卸载 C# 插件
  -> RustPluginBase.OnUnload()
    -> plugin_unload()
    -> 释放原生库句柄
```

`plugin_init` 返回 `0` 表示成功。返回非 `0` 时，C# 会记录错误码。建议 Rust 侧把可诊断的信息也写进日志。

## 请求处理

C# Controller 会把 ASP.NET 请求整理成 JSON 字符串，再调用：

```rust
plugin_handle_request(request_json, response_ptr, response_len)
```

Rust 的职责是：

1. 读取 `request_json`。
2. 反序列化成请求结构。
3. 根据 `method` 和 `sub_path` 分发到业务处理器。
4. 生成响应结构。
5. 序列化成 UTF-8 JSON。
6. 把响应字节交给 `response_ptr` 和 `response_len`。
7. 返回 `0`。

非 `0` 返回值会被 C# 转成 `500` 响应，例如：

```json
{
  "error": "RustBridge handler returned error code -2"
}
```

示例错误码：

```text
-1  request_json 不是有效 UTF-8 或指针不可读
-2  请求 JSON 解析失败
-3  响应 JSON 序列化失败
```

你可以使用自己的错误码，但建议保持负数，方便和正常 HTTP 状态码区分。

## 内存所有权

Rust 返回响应时，通常会把 `String` 转成 `Box<[u8]>`，再把裸指针交给 C#。

示例：

```rust
let bytes = response_json.into_bytes().into_boxed_slice();
let len = bytes.len();
let ptr = Box::into_raw(bytes) as *mut u8;

unsafe {
    *response_ptr = ptr;
    *response_len = len;
}
```

C# 读取完成后一定会调用：

```rust
plugin_free_response(ptr, len)
```

所以 Rust 必须用和分配方式匹配的方法释放内存。示例实现：

```rust
#[unsafe(no_mangle)]
pub extern "C" fn plugin_free_response(ptr: *mut u8, len: usize) {
    if ptr.is_null() {
        return;
    }

    unsafe {
        drop(Box::from_raw(std::slice::from_raw_parts_mut(ptr, len)));
    }
}
```

不要返回指向栈内存、临时字符串或全局可变缓冲区的指针。

## 请求 JSON

C# 传给 Rust 的 JSON 结构如下：

```json
{
  "method": "GET",
  "sub_path": "/servers/1",
  "query": "?foo=bar",
  "headers": {
    "Authorization": "Bearer xxx"
  },
  "body": "{}"
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `method` | string | HTTP 方法，例如 `GET`、`POST`。 |
| `sub_path` | string | 去掉插件路由前缀后的路径，始终以 `/` 开头。 |
| `query` | string | 原始查询字符串，可能为空，非空时通常以 `?` 开头。 |
| `headers` | object | 请求头，键和值都按字符串处理。 |
| `body` | string | 原始请求体字符串。JSON 请求体不会自动解析，需要 Rust 自己解析。 |

示例中的 `PluginRequest` 提供了两个辅助方法：

```rust
let body = req.body_json();
let query = req.query_params();
```

注意：示例 `query_params()` 是一个简单解析器，不会做 URL decode。如果你需要完整查询解析，建议在自己的 Rust 项目里引入成熟 crate。

## 响应 JSON

Rust 返回给 C# 的 JSON 结构如下：

```json
{
  "status": 200,
  "headers": {
    "X-Custom": "value"
  },
  "body": {
    "ok": true
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `status` | number | HTTP 状态码。当前示例总是序列化该字段。 |
| `headers` | object | 可选响应头。空对象会被省略。 |
| `body` | any | 响应体，可以是对象、数组、字符串、数字、布尔值或 `null`。 |

C# 的处理规则：

- `body` 不存在时，只返回状态码。
- `body` 是字符串时，直接把字符串作为响应体。
- `body` 是 JSON 对象、数组或其他 JSON 值时，交给 ASP.NET 返回。
- `headers` 中的每个属性都会写入 `Response.Headers`。

示例响应构造：

```rust
PluginResponse::ok(json!({
    "message": "Hello from Rust"
}))
```

带响应头：

```rust
PluginResponse::ok(json!({ "ok": true }))
    .with_header("X-Plugin", "rustbridge")
```

## 路由分发

示例路由在：

```text
samples/RustBridgeDemo/rust/src/router.rs
```

核心分发逻辑：

```rust
pub fn dispatch(req: PluginRequest, sdk: &SdkBridge) -> PluginResponse {
    match (req.method.as_str(), req.sub_path.as_str()) {
        ("GET", "/demo") => handle_demo_get(&req, sdk),
        ("GET", "/servers") => handle_servers_get(&req, sdk),
        ("POST", "/echo") => handle_echo(&req),
        _ => PluginResponse::not_found(),
    }
}
```

添加新接口时，只需要加一个 match 分支和一个处理函数。

## SDK 回调

Rust 不能直接引用 MSLX 的 .NET SDK。RustBridge 的做法是：Rust 把方法名和参数 JSON 传给 C#，C# 调用 MSLX SDK 后再把结果 JSON 返回给 Rust。

Rust 调用：

```rust
let servers = sdk.config_servers_get_list()?;
```

内部会变成：

```text
method:    config.servers.get_list
args_json: {}
```

C# 的 `RustPluginBase.HandleSdkCall` 收到后调用：

```csharp
MSLX.Config.Servers.GetServerList()
```

再把结果返回 Rust。

当前示例已封装的方法：

| Rust 方法 | SDK 方法 |
| --- | --- |
| `config_main_read()` | `MSLX.Config.Main.ReadConfig()` |
| `config_main_read_key(key)` | `MSLX.Config.Main.ReadConfigKey(key)` |
| `config_main_write_key(key, value)` | `MSLX.Config.Main.WriteConfigKey(key, value)` |
| `plugin_config_get_data_path()` | `this.Config().GetDataPath()` |
| `plugin_config_read()` | `this.Config().ReadConfig()` |
| `plugin_config_write(content)` | `this.Config().WriteConfig(content)` |
| `plugin_config_read_key(key)` | `this.Config().ReadConfigKey(key)` |
| `plugin_config_write_key(key, value)` | `this.Config().WriteConfigKey(key, value)` |
| `config_servers_get_list()` | `MSLX.Config.Servers.GetServerList()` |
| `config_servers_get_server(id)` | `MSLX.Config.Servers.GetServer(id)` |
| `config_servers_delete_server(id, delete_files)` | `MSLX.Config.Servers.DeleteServer(id, delete_files)` |
| `config_users_validate(username, password)` | `MSLX.Config.Users.ValidateUser(username, password)` |
| `config_users_get_by_api_key(key)` | `MSLX.Config.Users.GetUserByApiKey(key)` |
| `config_users_get_by_username(username)` | `MSLX.Config.Users.GetUserByUsername(username)` |
| `get_appdata_path()` | `MSLX.Config.GetAppDataPath()` |

`plugin_config_*` 是 MSLX SDK 1.4.3+ 的插件级配置接口。它读写的是当前插件自己的配置文件和数据目录；`config_main_*` 读写的是 MSLX 主配置，不建议用来保存插件自己的业务数据。

## 增加新的 SDK 方法

假设你想在 Rust 里调用一个新的 SDK 方法，推荐步骤如下。

第一步，在 C# 插件入口中覆盖 `HandleSdkCall`：

```csharp
protected override string HandleSdkCall(string method, string argsJson)
{
    if (method == "my.new_method")
    {
        // 解析 argsJson，调用 MSLX SDK，返回 JSON 字符串
        return "{\"ok\":true}";
    }

    return base.HandleSdkCall(method, argsJson);
}
```

第二步，在 Rust 的 `SdkBridge` 中加包装：

```rust
pub fn my_new_method(&self) -> Result<Value, String> {
    self.call("my.new_method", json!({}))
}
```

第三步，在业务处理器中调用：

```rust
match sdk.my_new_method() {
    Ok(value) => PluginResponse::ok(value),
    Err(err) => PluginResponse::internal_error(&err),
}
```

## 线程和状态

当前示例用 `OnceLock<SdkBridge>` 保存 SDK 回调。这样做适合插件生命周期内只初始化一次的场景。

如果你要保存更多状态，例如数据库连接、后台任务句柄或缓存，建议：

- 在 `plugin_init` 中初始化。
- 在 `plugin_unload` 中关闭或释放。
- 使用 `Mutex`、`RwLock` 或异步运行时提供的同步结构保护共享状态。
- 不要在请求处理中直接使用未同步的全局可变状态。

## 安全边界

FFI 层无法由 Rust 编译器完全保护。建议遵守这些规则：

- 所有来自 C# 的指针都先判空或放在最小 `unsafe` 范围内处理。
- 所有字符串都按 UTF-8 解析，失败时返回错误码。
- Rust 分配的响应内存只由 `plugin_free_response` 释放。
- 不要 panic 穿过 FFI 边界。业务错误应该转成 `PluginResponse::error(...)` 或错误码。

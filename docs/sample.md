# Sample Plugin

示例插件位于：

```text
samples/RustBridgeDemo
```

它是一个完整的 MSLX 插件，可以作为模板复制，也可以作为理解 RustBridge 的参考实现。

## 目录结构

```text
samples/RustBridgeDemo/
├── MSLX.Plugin.RustBridge.Demo.csproj
├── RustPluginEntry.cs
├── RustBridgeController.cs
└── rust/
    ├── Cargo.toml
    └── src/
        ├── bridge.rs
        ├── lib.rs
        ├── router.rs
        └── sdk.rs
```

## C# 部分

`RustPluginEntry.cs`：

- 继承 `RustPluginBase`。
- 设置插件 ID、名称、版本等元数据。
- 保存 `Instance`，供 Controller 使用。

当前插件 ID：

```text
mslx-plugin-rustbridge-demo
```

`RustBridgeController.cs`：

- 继承 `RustBridgeControllerBase`。
- 定义插件 API 路由前缀。
- 把请求交给 `RustPluginEntry.Instance`。

当前路由前缀：

```text
api/plugins/mslx-plugin-rustbridge-demo
```

## Rust 部分

`lib.rs`：

- 导出 FFI 函数。
- 初始化 SDK 回调。
- 调用 `router::dispatch` 处理请求。

`bridge.rs`：

- 定义 `PluginRequest`。
- 定义 `PluginResponse`。
- 提供常用响应构造方法。

`sdk.rs`：

- 保存 C# 传入的日志和 SDK 回调。
- 把 Rust 方法转成 SDK 回调。

`router.rs`：

- 放示例路由和业务逻辑。
- 这是写业务时最常改的文件。

## 示例路由

### GET /demo

请求：

```text
GET /api/plugins/mslx-plugin-rustbridge-demo/demo
```

返回示例：

```json
{
  "plugin": "mslx-plugin-rustbridge-demo",
  "message": "Hello from RustBridge!"
}
```

### GET /servers

请求：

```text
GET /api/plugins/mslx-plugin-rustbridge-demo/servers
```

Rust 会调用：

```rust
sdk.config_servers_get_list()
```

然后返回 MSLX SDK 给出的服务器列表。

### GET /servers/{id}

请求：

```text
GET /api/plugins/mslx-plugin-rustbridge-demo/servers/1
```

Rust 会解析路径中的 `id`，再调用：

```rust
sdk.config_servers_get_server(id)
```

### POST /echo

请求：

```text
POST /api/plugins/mslx-plugin-rustbridge-demo/echo
```

Rust 会把收到的方法、路径、查询字符串和 body 返回，适合测试前后端通信。

## 构建逻辑

示例 `.csproj` 中包含三个关键目标。

### BuildRust

执行：

```bash
cargo build --release
```

工作目录是：

```text
samples/RustBridgeDemo/rust
```

### CopyRustLib

把 Rust 产物复制到 .NET 输出目录。

Linux：

```text
rust/target/release/libmslx_plugin_rustbridge.so
```

Windows：

```text
rust/target/release/mslx_plugin_rustbridge.dll
```

macOS：

```text
rust/target/release/libmslx_plugin_rustbridge.dylib
```

### MergeDependencies

Release 构建时使用 ILRepack 合并托管依赖，方便部署。合并完成后，输出目录通常只需要：

```text
MSLX.Plugin.RustBridge.Demo.dll
libmslx_plugin_rustbridge.so
```

## 从示例改成自己的插件

建议修改：

- `RustPluginEntry.Id`
- `RustPluginEntry.Name`
- `RustPluginEntry.Description`
- `RustPluginEntry.Version`
- `RustBridgeController` 的 `[Route]`
- `.csproj` 的 `AssemblyName`
- `.csproj` 的 `RootNamespace`
- `.csproj` 的 `RustLibName`
- `rust/Cargo.toml` 的 package name 和 `[lib] name`
- `rust/src/router.rs` 的业务逻辑

如果你只是先验证技术链路，可以只改插件 ID 和路由。

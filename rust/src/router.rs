use crate::bridge::{PluginRequest, PluginResponse};
use crate::sdk::SdkBridge;
use serde_json::json;

/// 插件初始化时调用（可用于启动后台任务、建立连接等）
pub fn init(_sdk: &'static SdkBridge) {
    // 此处暂不启用
    // 示例：读取 appdata 路径
    // let path = sdk.get_appdata_path().unwrap_or_default();
    // sdk.info(&format!("AppData path: {path}"));
}

pub fn shutdown() {}

pub fn dispatch(req: PluginRequest, sdk: &SdkBridge) -> PluginResponse {
    match (req.method.as_str(), req.sub_path.as_str()) {

        ("GET", "/demo") => handle_demo_get(&req, sdk),

        ("GET", "/servers") => handle_servers_get(&req, sdk),

        ("GET", path) if path.starts_with("/servers/") => {
            let id_str = &path["/servers/".len()..];
            match id_str.parse::<u32>() {
                Ok(id) => handle_server_get(&req, id, sdk),
                Err(_) => PluginResponse::bad_request("Invalid server id"),
            }
        }

        ("POST", "/echo") => handle_echo(&req),

        _ => PluginResponse::not_found(),
    }
}

fn handle_demo_get(_req: &PluginRequest, sdk: &SdkBridge) -> PluginResponse {
    sdk.debug("handle_demo_get called");
    PluginResponse::ok(json!({
        "plugin": "mslx-plugin-rustbridge",
        "message": "Hello from RustBridge!",
    }))
}

fn handle_servers_get(_req: &PluginRequest, sdk: &SdkBridge) -> PluginResponse {
    match sdk.config_servers_get_list() {
        Ok(list) => PluginResponse::ok(list),
        Err(e) => {
            sdk.error(&format!("Failed to get server list: {e}"));
            PluginResponse::internal_error(&e)
        }
    }
}

fn handle_server_get(_req: &PluginRequest, id: u32, sdk: &SdkBridge) -> PluginResponse {
    match sdk.config_servers_get_server(id) {
        Ok(server) => PluginResponse::ok(server),
        Err(_e) => PluginResponse::not_found(),
    }
}

fn handle_echo(req: &PluginRequest) -> PluginResponse {
    PluginResponse::ok(json!({
        "method":   req.method,
        "sub_path": req.sub_path,
        "query":    req.query,
        "body":     req.body_json(),
    }))
}

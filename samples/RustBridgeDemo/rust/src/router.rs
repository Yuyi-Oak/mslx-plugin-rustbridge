use crate::bridge::{PluginRequest, PluginResponse};
use crate::sdk::SdkBridge;
use serde_json::json;

pub fn init(_sdk: &'static SdkBridge) {}

pub fn shutdown() {}

pub fn dispatch(req: PluginRequest, sdk: &SdkBridge) -> PluginResponse {
    match (req.method.as_str(), req.sub_path.as_str()) {

        ("GET", "/demo") => handle_demo_get(&req, sdk),

        ("GET", "/servers") => handle_servers_get(&req, sdk),

        ("GET", path) if path.starts_with("/servers/") => handle_server_path(&req, path, sdk),

        ("POST", "/echo") => handle_echo(&req),

        _ => PluginResponse::not_found(),
    }
}

fn handle_demo_get(_req: &PluginRequest, sdk: &SdkBridge) -> PluginResponse {
    sdk.debug("handle_demo_get called");
    PluginResponse::ok(json!({
        "plugin": "mslx-plugin-rustbridge-demo",
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

fn handle_server_path(req: &PluginRequest, path: &str, sdk: &SdkBridge) -> PluginResponse {
    match parse_server_id(path) {
        Ok(id) => handle_server_get(req, id, sdk),
        Err(_) => PluginResponse::bad_request("Invalid server id"),
    }
}

fn parse_server_id(path: &str) -> Result<u32, std::num::ParseIntError> {
    path["/servers/".len()..].parse::<u32>()
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

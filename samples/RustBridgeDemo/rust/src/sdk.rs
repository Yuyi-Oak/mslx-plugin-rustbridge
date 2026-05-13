use std::ffi::{CStr, CString, c_char};
use serde_json::{json, Value};
use crate::{LogFn, LogErrFn, SdkCallFn};

#[allow(dead_code)]
pub struct SdkBridge {
    log_info:    LogFn,
    log_warn:    LogFn,
    log_error:   LogFn,
    log_error_ex: LogErrFn,
    log_debug:   LogFn,
    sdk_call:    SdkCallFn,
}

unsafe impl Send for SdkBridge {}
unsafe impl Sync for SdkBridge {}

#[allow(dead_code)]
impl SdkBridge {
    pub fn new(
        log_info: LogFn, log_warn: LogFn, log_error: LogFn,
        log_error_ex: LogErrFn, log_debug: LogFn, sdk_call: SdkCallFn,
    ) -> Self {
        Self { log_info, log_warn, log_error, log_error_ex, log_debug, sdk_call }
    }

    pub fn info(&self, msg: &str) {
        let s = CString::new(msg).unwrap_or_default();
        (self.log_info)(s.as_ptr());
    }
    pub fn warn(&self, msg: &str) {
        let s = CString::new(msg).unwrap_or_default();
        (self.log_warn)(s.as_ptr());
    }
    pub fn error(&self, msg: &str) {
        let s = CString::new(msg).unwrap_or_default();
        (self.log_error)(s.as_ptr());
    }
    pub fn error_ex(&self, msg: &str, ex: &str) {
        let m = CString::new(msg).unwrap_or_default();
        let e = CString::new(ex).unwrap_or_default();
        (self.log_error_ex)(m.as_ptr(), e.as_ptr());
    }
    pub fn debug(&self, msg: &str) {
        let s = CString::new(msg).unwrap_or_default();
        (self.log_debug)(s.as_ptr());
    }

    fn call_raw(&self, method: &str, args: &Value) -> Result<String, String> {
        let m = CString::new(method).map_err(|e| e.to_string())?;
        let a = CString::new(args.to_string()).map_err(|e| e.to_string())?;

        let raw_ptr: *const c_char = (self.sdk_call)(m.as_ptr(), a.as_ptr());
        if raw_ptr.is_null() {
            return Err("sdk_call returned null".into());
        }
        let result = unsafe { CStr::from_ptr(raw_ptr) }
            .to_str()
            .map(String::from)
            .map_err(|e| e.to_string())?;
        Ok(result)
    }

    fn call(&self, method: &str, args: Value) -> Result<Value, String> {
        let raw = self.call_raw(method, &args)?;
        serde_json::from_str(&raw).map_err(|e| e.to_string())
    }

    pub fn config_main_read(&self) -> Result<Value, String> {
        self.call("config.main.read", json!({}))
    }

    pub fn config_main_read_key(&self, key: &str) -> Result<Value, String> {
        self.call("config.main.read_key", json!({ "key": key }))
    }

    pub fn config_main_write_key(&self, key: &str, value: Value) -> Result<(), String> {
        self.call("config.main.write_key", json!({ "key": key, "value": value }))?;
        Ok(())
    }

    pub fn plugin_config_get_data_path(&self) -> Result<String, String> {
        let v = self.call("plugin.config.get_data_path", json!({}))?;
        Ok(v.as_str().unwrap_or("").to_string())
    }

    pub fn plugin_config_read(&self) -> Result<Value, String> {
        self.call("plugin.config.read", json!({}))
    }

    pub fn plugin_config_write(&self, content: Value) -> Result<(), String> {
        self.call("plugin.config.write", json!({ "content": content }))?;
        Ok(())
    }

    pub fn plugin_config_read_key(&self, key: &str) -> Result<Value, String> {
        self.call("plugin.config.read_key", json!({ "key": key }))
    }

    pub fn plugin_config_write_key(&self, key: &str, value: Value) -> Result<(), String> {
        self.call("plugin.config.write_key", json!({ "key": key, "value": value }))?;
        Ok(())
    }

    pub fn config_servers_get_list(&self) -> Result<Value, String> {
        self.call("config.servers.get_list", json!({}))
    }

    pub fn config_servers_get_server(&self, id: u32) -> Result<Value, String> {
        self.call("config.servers.get_server", json!({ "id": id }))
    }

    pub fn config_servers_delete_server(&self, id: u32, delete_files: bool) -> Result<bool, String> {
        let v = self.call("config.servers.delete_server",
            json!({ "id": id, "delete_files": delete_files }))?;
        Ok(v["result"].as_bool().unwrap_or(false))
    }

    pub fn config_users_validate(&self, username: &str, password: &str) -> Result<bool, String> {
        let v = self.call("config.users.validate",
            json!({ "username": username, "password": password }))?;
        Ok(v["result"].as_bool().unwrap_or(false))
    }

    pub fn config_users_get_by_api_key(&self, key: &str) -> Result<Value, String> {
        self.call("config.users.get_by_api_key", json!({ "key": key }))
    }

    pub fn config_users_get_by_username(&self, username: &str) -> Result<Value, String> {
        self.call("config.users.get_by_username", json!({ "username": username }))
    }

    pub fn get_appdata_path(&self) -> Result<String, String> {
        let v = self.call("config.get_appdata_path", json!({}))?;
        Ok(v.as_str().unwrap_or("").to_string())
    }
}

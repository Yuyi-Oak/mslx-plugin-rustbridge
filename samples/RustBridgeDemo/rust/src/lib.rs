use std::ffi::{CStr, c_char};
use std::sync::OnceLock;

mod bridge;
mod sdk;
mod router;

pub use bridge::{PluginRequest, PluginResponse};

static SDK: OnceLock<sdk::SdkBridge> = OnceLock::new();

pub type LogFn    = extern "C" fn(*const c_char);
pub type LogErrFn = extern "C" fn(*const c_char, *const c_char);
pub type SdkCallFn = extern "C" fn(*const c_char, *const c_char) -> *const c_char;

#[unsafe(no_mangle)]
pub extern "C" fn plugin_init(
    log_info:    LogFn,
    log_warn:    LogFn,
    log_error:   LogFn,
    log_error_ex: LogErrFn,
    log_debug:   LogFn,
    sdk_call:    SdkCallFn,
) -> i32 {
    let bridge = sdk::SdkBridge::new(
        log_info, log_warn, log_error, log_error_ex, log_debug, sdk_call,
    );

    if SDK.set(bridge).is_err() {
        return -1;
    }

    let Some(sdk) = SDK.get() else {
        return -2;
    };

    sdk.info("RustBridge plugin initialized.");
    router::init(sdk);

    0
}

#[unsafe(no_mangle)]
pub extern "C" fn plugin_unload() {
    if let Some(sdk) = SDK.get() {
        sdk.info("Rust plugin unloading.");
    }
    router::shutdown();
}

#[unsafe(no_mangle)]
pub extern "C" fn plugin_handle_request(
    request_json: *const c_char,
    response_ptr: *mut *mut u8,
    response_len: *mut usize,
) -> i32 {
    let Some(sdk) = SDK.get() else {
        return -4;
    };

    let json_str = match unsafe { CStr::from_ptr(request_json) }.to_str() {
        Ok(s) => s,
        Err(_) => return -1,
    };

    let request: PluginRequest = match serde_json::from_str(json_str) {
        Ok(r) => r,
        Err(e) => {
            sdk.error(&format!("Failed to parse request JSON: {e}"));
            return -2;
        }
    };

    let response = router::dispatch(request, sdk);

    let response_json = match serde_json::to_string(&response) {
        Ok(s) => s,
        Err(e) => {
            sdk.error(&format!("Failed to serialize response: {e}"));
            return -3;
        }
    };

    let bytes = response_json.into_bytes().into_boxed_slice();
    let len = bytes.len();
    let ptr = Box::into_raw(bytes) as *mut u8;

    unsafe {
        *response_ptr = ptr;
        *response_len = len;
    }

    0
}

#[unsafe(no_mangle)]
pub extern "C" fn plugin_free_response(ptr: *mut u8, len: usize) {
    if ptr.is_null() { return; }
    unsafe {
        drop(Box::from_raw(std::slice::from_raw_parts_mut(ptr, len)));
    }
}

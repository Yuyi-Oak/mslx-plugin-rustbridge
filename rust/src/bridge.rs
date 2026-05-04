use std::collections::HashMap;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize)]
pub struct PluginRequest {
    pub method: String,
    pub sub_path: String,
    #[serde(default)]
    pub query: String,
    #[serde(default)]
    pub headers: HashMap<String, String>,
    #[serde(default)]
    pub body: String,
}

impl PluginRequest {
    pub fn body_json(&self) -> serde_json::Value {
        if self.body.is_empty() {
            serde_json::Value::Null
        } else {
            serde_json::from_str(&self.body).unwrap_or(serde_json::Value::Null)
        }
    }

    pub fn query_params(&self) -> HashMap<String, String> {
        let qs = self.query.trim_start_matches('?');
        qs.split('&')
            .filter_map(|pair| {
                let mut kv = pair.splitn(2, '=');
                let k = kv.next()?.to_string();
                let v = kv.next().unwrap_or("").to_string();
                if k.is_empty() { None } else { Some((k, v)) }
            })
            .collect()
    }
}

#[derive(Debug, Serialize)]
pub struct PluginResponse {
    pub status: u16,
    #[serde(skip_serializing_if = "HashMap::is_empty")]
    pub headers: HashMap<String, String>,
    pub body: serde_json::Value,
}

impl PluginResponse {
    pub fn ok(body: impl Serialize) -> Self {
        Self {
            status: 200,
            headers: HashMap::new(),
            body: serde_json::to_value(body).unwrap_or(serde_json::Value::Null),
        }
    }

    pub fn error(status: u16, message: &str) -> Self {
        Self {
            status,
            headers: HashMap::new(),
            body: serde_json::json!({ "error": message }),
        }
    }

    pub fn not_found() -> Self {
        Self::error(404, "Not found")
    }

    pub fn bad_request(msg: &str) -> Self {
        Self::error(400, msg)
    }

    pub fn internal_error(msg: &str) -> Self {
        Self::error(500, msg)
    }

    pub fn with_header(mut self, key: &str, value: &str) -> Self {
        self.headers.insert(key.to_string(), value.to_string());
        self
    }
}

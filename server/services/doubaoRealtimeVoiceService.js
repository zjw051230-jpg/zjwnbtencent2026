const WebSocket = require("ws");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const DOUBAO_REALTIME_URL = "wss://openspeech.bytedance.com/api/v3/realtime/dialogue";
const DEFAULT_RESOURCE_ID = "volc.speech.dialog";
const DEFAULT_APP_KEY = "PlgvMymc7f3tQnJ6";
const DEFAULT_MODEL = "1.2.1.1";
const DEFAULT_SPEAKER = "zh_male_yunzhou_jupiter_bigtts";

async function createDoubaoVoiceDialogue({
  audioPath,
  roleId,
  roleName,
  sceneId,
  outputDir,
  publicBaseUrl
}) {
  const config = readDoubaoRealtimeConfig();
  const missingConfig = getMissingRequiredConfig(config);
  const connectId = crypto.randomUUID();
  const sessionId = crypto.randomUUID();
  const headers = buildWebSocketHeaders(config, connectId);
  const startSessionPayload = buildStartSessionPayload({
    config,
    sessionId,
    roleId,
    roleName,
    sceneId
  });

  void WebSocket;
  void fs;
  void path;
  void audioPath;
  void outputDir;
  void publicBaseUrl;
  void headers;
  void startSessionPayload;

  return {
    ok: false,
    transcript: "",
    replyText: "豆包实时语音服务骨架已创建，但尚未接入完整 WebSocket 二进制协议。",
    audioUrl: "",
    speakingVideoNodeId: "role_speaking",
    emotion: "calm",
    error: missingConfig.length > 0
      ? `DOUBAO_REALTIME_MISSING_CONFIG: ${missingConfig.join(", ")}`
      : "DOUBAO_REALTIME_NOT_IMPLEMENTED"
  };
}

function readDoubaoRealtimeConfig() {
  return {
    appId: getEnv("DOUBAO_REALTIME_APP_ID"),
    accessKey: getEnv("DOUBAO_REALTIME_ACCESS_KEY"),
    resourceId: getEnv("DOUBAO_REALTIME_RESOURCE_ID") || DEFAULT_RESOURCE_ID,
    appKey: getEnv("DOUBAO_REALTIME_APP_KEY") || DEFAULT_APP_KEY,
    model: getEnv("DOUBAO_REALTIME_MODEL") || DEFAULT_MODEL,
    speaker: getEnv("DOUBAO_REALTIME_SPEAKER") || DEFAULT_SPEAKER
  };
}

function getMissingRequiredConfig(config) {
  const missing = [];

  if (!config.appId) {
    missing.push("DOUBAO_REALTIME_APP_ID");
  }

  if (!config.accessKey) {
    missing.push("DOUBAO_REALTIME_ACCESS_KEY");
  }

  return missing;
}

function buildWebSocketHeaders(config, connectId) {
  return {
    "X-Api-App-ID": config.appId,
    "X-Api-Access-Key": config.accessKey,
    "X-Api-Resource-Id": config.resourceId,
    "X-Api-App-Key": config.appKey,
    "X-Api-Connect-Id": connectId
  };
}

function buildStartSessionPayload({ config, sessionId, roleId, roleName, sceneId }) {
  return {
    event: "StartSession",
    session: {
      id: sessionId
    },
    dialog: {
      bot_name: roleName || roleId || "role",
      extra: {
        input_mod: "audio_file",
        role_id: roleId || "",
        scene_id: sceneId || ""
      }
    },
    tts: {
      speaker: config.speaker
    },
    llm: {
      model: config.model
    },
    audio: {
      format: "pcm",
      channel: 1,
      sample_rate: 16000,
      sample_format: "s16le",
      packet_size_bytes: 640
    }
  };
}

function getEnv(name) {
  return typeof process.env[name] === "string" ? process.env[name].trim() : "";
}

// TODO:
// - buildClientEventFrame: encode StartConnection / StartSession / FinishSession events.
// - buildAudioFrame: wrap 16k mono int16 little-endian PCM chunks into protocol frames.
// - parseServerFrame: decode server binary frames and extract event payloads.
// - Convert uploaded wav to 16k mono PCM int16 little-endian before sending.
// - Split PCM into 20ms packets. At 16k / int16 / mono, each packet is 640 bytes.
// - Send audio packets over the Doubao Realtime WebSocket.
// - Collect ASRResponse events as transcript text.
// - Collect ChatResponse events as model reply text.
// - Collect TTSResponse binary audio chunks.
// - Detect TTSEnded to finish a dialogue turn.
// - Save returned TTS audio into outputDir.
// - Return Unity-compatible JSON: transcript, replyText, audioUrl, speakingVideoNodeId, emotion.

module.exports = {
  createDoubaoVoiceDialogue
};

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const DOUBAO_TTS_URL = "https://openspeech.bytedance.com/api/v1/tts";
const DEFAULT_APP_ID = "4047318685";
const DEFAULT_APP_KEY = "PlgvMymc7f3tQnJ6";
const DEFAULT_SPEAKER = "BV700_streaming";
const DEFAULT_CLUSTER = "volcano_tts";
const TTS_TIMEOUT_MS = 5000;

async function createDoubaoTtsAudio({
  text,
  outputDir,
  publicBaseUrl,
  speaker = DEFAULT_SPEAKER,
  runtimeConfig = {}
}) {
  const normalizedText = toStringValue(text);
  if (!normalizedText) {
    throw new Error("DOUBAO_TTS_TEXT_EMPTY");
  }

  if (!outputDir || !publicBaseUrl) {
    throw new Error("DOUBAO_TTS_OUTPUT_CONFIG_MISSING");
  }

  const config = resolveTtsConfig(runtimeConfig);
  if (!config.accessKey) {
    throw new Error("DOUBAO_TTS_ACCESS_KEY_MISSING");
  }

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), TTS_TIMEOUT_MS);
  const effectiveSpeaker = resolveTtsSpeaker(speaker);
  const requestBody = buildTtsRequestBody({
    appId: config.appId,
    accessKey: config.accessKey,
    speaker: effectiveSpeaker,
    text: normalizedText,
    cluster: config.cluster
  });

  console.log(`[doubao-post-tts] endpoint=${DOUBAO_TTS_URL}`);
  console.log(`[doubao-post-tts] speaker=${effectiveSpeaker}`);
  console.log(`[doubao-post-tts] textLength=${normalizedText.length}`);
  console.log(`[doubao-post-tts] requestKeys=${Object.keys(requestBody).join(",")}`);
  console.log(`[doubao-post-tts] appKeys=${Object.keys(requestBody.app || {}).join(",")}`);
  console.log(`[doubao-post-tts] audioKeys=${Object.keys(requestBody.audio || {}).join(",")}`);

  try {
    const response = await fetch(DOUBAO_TTS_URL, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Authorization": `Bearer;${config.accessKey}`
      },
      body: JSON.stringify(requestBody),
      signal: controller.signal
    });

    const contentType = response.headers.get("content-type") || "";
    const responseBuffer = Buffer.from(await response.arrayBuffer());
    console.log(`[doubao-post-tts] status=${response.status}`);

    if (!response.ok) {
      const safeBody = buildSafeResponseBodySummary(responseBuffer, contentType);
      console.warn(`[doubao-post-tts] responseBody=${safeBody}`);
      throw new Error(`DOUBAO_TTS_HTTP_${response.status}:${safeBody}`);
    }

    const audioBuffer = parseTtsAudioBuffer(responseBuffer, contentType);
    if (!audioBuffer || audioBuffer.length === 0) {
      throw new Error("DOUBAO_TTS_AUDIO_EMPTY");
    }

    await fs.promises.mkdir(outputDir, { recursive: true });
    const fileName = buildDoubaoTtsFileName();
    const outputPath = path.join(outputDir, fileName);
    await fs.promises.writeFile(outputPath, audioBuffer);

    const audioUrl = buildGeneratedAudioUrl(publicBaseUrl, fileName);
    console.log(`[doubao-post-tts] wrote audio file=${outputPath} bytes=${audioBuffer.length}`);
    console.log(`[doubao-post-tts] audioUrl=${audioUrl}`);

    return {
      fileName,
      filePath: outputPath,
      audioUrl,
      bytes: audioBuffer.length
    };
  } finally {
    clearTimeout(timeoutId);
  }
}

function buildTtsRequestBody({ appId, accessKey, speaker, text, cluster }) {
  return {
    app: {
      appid: appId,
      token: accessKey,
      cluster: cluster || DEFAULT_CLUSTER
    },
    user: {
      uid: "unity-local"
    },
    audio: {
      voice_type: speaker || DEFAULT_SPEAKER,
      encoding: "wav",
      rate: 24000,
      compression_rate: 1,
      speed_ratio: 1.0,
      volume_ratio: 1.0,
      pitch_ratio: 1.0,
      language: "cn"
    },
    request: {
      reqid: crypto.randomUUID(),
      text,
      text_type: "plain",
      operation: "query",
      silence_duration: 125
    }
  };
}

function parseTtsAudioBuffer(responseBuffer, contentType) {
  if (contentType.includes("application/json") || looksLikeJson(responseBuffer)) {
    const payload = JSON.parse(responseBuffer.toString("utf8"));
    const code = Number(payload.code || payload.Code || 0);
    if (code && code !== 3000) {
      throw new Error(`DOUBAO_TTS_CODE_${code}`);
    }

    const base64Audio = payload.data || payload.audio || payload.result && payload.result.audio;
    if (!base64Audio || typeof base64Audio !== "string") {
      throw new Error("DOUBAO_TTS_JSON_AUDIO_MISSING");
    }

    return Buffer.from(base64Audio, "base64");
  }

  return responseBuffer;
}

function looksLikeJson(buffer) {
  if (!buffer || buffer.length === 0) {
    return false;
  }

  const firstByte = buffer.find((byte) => byte > 32);
  return firstByte === 0x7b || firstByte === 0x5b;
}

function resolveTtsConfig(runtimeConfig = {}) {
  return {
    appId: toStringValue(runtimeConfig.appId) || getEnv("DOUBAO_TTS_APP_ID") || getEnv("DOUBAO_REALTIME_APP_ID") || DEFAULT_APP_ID,
    appKey: toStringValue(runtimeConfig.appKey) || getEnv("DOUBAO_REALTIME_APP_KEY") || DEFAULT_APP_KEY,
    accessKey: toStringValue(runtimeConfig.accessKey) || getEnv("DOUBAO_TTS_ACCESS_TOKEN") || getEnv("DOUBAO_REALTIME_ACCESS_KEY"),
    cluster: getEnv("DOUBAO_TTS_CLUSTER_ID") || DEFAULT_CLUSTER
  };
}

function resolveTtsSpeaker(speaker) {
  const configuredSpeaker = getEnv("DOUBAO_TTS_SPEAKER") || getEnv("DOUBAO_TTS_VOICE_TYPE");
  if (configuredSpeaker) {
    return configuredSpeaker;
  }

  const normalizedSpeaker = toStringValue(speaker);
  if (normalizedSpeaker && !normalizedSpeaker.includes("_bigtts")) {
    return normalizedSpeaker;
  }

  return DEFAULT_SPEAKER;
}

function buildSafeResponseBodySummary(responseBuffer, contentType) {
  const bodyText = responseBuffer.toString("utf8").replace(/\s+/g, " ").trim();
  if (!bodyText) {
    return `empty contentType=${contentType || "unknown"}`;
  }

  return bodyText.slice(0, 1000);
}

function buildDoubaoTtsFileName() {
  return `doubao-tts-post-${Date.now()}-${crypto.randomUUID()}.wav`;
}

function buildGeneratedAudioUrl(publicBaseUrl, fileName) {
  return `${toStringValue(publicBaseUrl).replace(/\/$/, "")}/generated-audio/${fileName}`;
}

function toStringValue(value) {
  return typeof value === "string" ? value.trim() : "";
}

function getEnv(name) {
  return typeof process.env[name] === "string" ? process.env[name].trim() : "";
}

module.exports = {
  createDoubaoTtsAudio
};

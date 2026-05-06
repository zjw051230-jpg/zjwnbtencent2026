const WebSocket = require("ws");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");
const { createDoubaoTtsAudio } = require("./doubaoTextToSpeechService.js");

const DOUBAO_REALTIME_URL = "wss://openspeech.bytedance.com/api/v3/realtime/dialogue";
const DEFAULT_RESOURCE_ID = "volc.speech.dialog";
const DEFAULT_APP_ID = "4047318685";
const DEFAULT_APP_KEY = "PlgvMymc7f3tQnJ6";
const DEFAULT_MODEL = "1.2.1.1";
const DEFAULT_SPEAKER = "zh_male_yunzhou_jupiter_bigtts";
const DEFAULT_SYSTEM_ROLE = "你是互动影像游戏中的角色。你冷静、克制、善于观察线索。请用简短自然的中文回应玩家。";
const DEFAULT_SPEAKING_STYLE = "语气沉稳，句子不要太长，像影视角色对白。";
const AUDIO_PACKET_SIZE_BYTES = 640;
const AUDIO_PACKET_INTERVAL_MS = 20;
const SESSION_TIMEOUT_MS = 60000;
const CONNECTION_STARTED_TIMEOUT_MS = 8000;
const SESSION_STARTED_TIMEOUT_MS = 10000;
const ASR_CHAT_TIMEOUT_MS = 60000;
const DOUBAO_QPM_COOLDOWN_MS = 60000;
const TTS_FINALIZATION_WAIT_MS = 5000;
const HTTP_TTS_ENABLED = process.env.DOUBAO_HTTP_TTS_ENABLED === "true";
const TTS_SAMPLE_RATE = 24000;
const TTS_CHANNELS = 1;
const TTS_BITS_PER_SAMPLE = 16;

let doubaoCooldownUntil = 0;

const MESSAGE_TYPE = {
  CLIENT_FULL_REQUEST: 0x1,
  CLIENT_AUDIO_ONLY_REQUEST: 0x2,
  SERVER_FULL_RESPONSE: 0x9,
  SERVER_AUDIO_ONLY_RESPONSE: 0xb,
  SERVER_LEGACY_AUDIO_ONLY_RESPONSE: 0xa,
  SERVER_ACK: 0xc,
  SERVER_ERROR_RESPONSE: 0xf
};

const SERIALIZATION = {
  NONE: 0x0,
  JSON: 0x1
};

const COMPRESSION = {
  NONE: 0x0,
  GZIP: 0x1
};

const CLIENT_FLAGS_WITH_EVENT = 0x4;

const CLIENT_EVENT = {
  StartConnection: 1,
  FinishConnection: 2,
  StartSession: 100,
  FinishSession: 102,
  TaskRequest: 200,
  EndASR: 400
};

const SERVER_EVENT_NAME = {
  50: "ConnectionStarted",
  51: "ConnectionFailed",
  52: "ConnectionFinished",
  150: "SessionStarted",
  152: "SessionFinished",
  153: "SessionFailed",
  350: "TTSSentenceStart",
  351: "TTSSentenceEnd",
  352: "TTSResponse",
  359: "TTSEnded",
  450: "ASRInfo",
  451: "ASRResponse",
  459: "ASREnded",
  550: "ChatResponse",
  559: "ChatEnded",
  599: "DialogCommonError"
};

const CLIENT_EVENT_NAME = Object.fromEntries(
  Object.entries(CLIENT_EVENT).map(([name, id]) => [id, name])
);

async function createDoubaoVoiceDialogue({
  audioPath,
  roleId,
  roleName,
  sceneId,
  outputDir,
  publicBaseUrl,
  runtimeConfig
}) {
  const config = readDoubaoRealtimeConfig(runtimeConfig);
  const missingConfig = getMissingRequiredConfig(config);

  if (missingConfig.length > 0) {
    return createFailureResponse(`DOUBAO_REALTIME_MISSING_CONFIG: ${missingConfig.join(", ")}`);
  }

  if (isDoubaoCooldownActive()) {
    return createFailureResponse("doubao qpm cooldown");
  }

  try {
    const result = await runDoubaoRealtimeSession({
      audioPath,
      roleId,
      roleName,
      sceneId,
      outputDir,
      publicBaseUrl,
      config,
      runtimeConfig
    });

    if (!result || typeof result !== "object") {
      return createFailureResponse("DOUBAO_REALTIME_EMPTY_RESPONSE");
    }

    if (result.ok) {
      return {
        ok: true,
        transcript: toStringValue(result.transcript),
        replyText: toStringValue(result.replyText),
        audioUrl: toStringValue(result.audioUrl),
        speakingVideoNodeId: "role_speaking",
        emotion: "calm",
        source: toStringValue(result.source) || "doubao",
        error: toStringValue(result.error)
      };
    }

    const errorMessage = toStringValue(result.error) || "DOUBAO_REALTIME_FAILED";
    maybeEnableDoubaoCooldown(errorMessage);
    return createFailureResponse(errorMessage);
  } catch (error) {
    const errorMessage = getErrorMessage(error);
    maybeEnableDoubaoCooldown(errorMessage);
    return createFailureResponse(errorMessage);
  }
}

async function runDoubaoRealtimeSession({
  audioPath,
  roleId,
  roleName,
  sceneId,
  outputDir,
  publicBaseUrl,
  config,
  runtimeConfig = {}
}) {
  const wav = await readWavPcm16(audioPath);
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

  console.log(`[doubao] connectId=${connectId} sessionId=${sessionId} wavDurationMs=${wav.durationMs}`);
  console.log(
    `[doubao] ws handshake config appIdConfigured=${Boolean(config.appId)} accessKeyConfigured=${Boolean(config.accessKey)} resourceIdConfigured=${Boolean(config.resourceId)} appKeyConfigured=${Boolean(config.appKey)} appKeyLooksDefault=${config.appKey === DEFAULT_APP_KEY} connectId=${connectId}`
  );

  return new Promise((resolve) => {
    let settled = false;
    let lastLogId = "";
    let transcript = "";
    let finalTranscript = "";
    let replyText = "";
    let fallbackReplyText = "";
    let connectionStarted = false;
    let sessionStarted = false;
    let chatEnded = false;
    let ttsEnded = false;
    let asrEnded = false;
    let asrInfoReceived = false;
    let asrResponseReceived = false;
    let chatResponseReceived = false;
    let dialogCommonErrorReceived = false;
    let receivedAnyServerFrame = false;
    let endAsrSent = false;
    let finishedSendingAudio = false;
    let finishSessionSent = false;
    let transportError = "";
    let waitStage = "connection_started";
    let ttsChunkCount = 0;
    let ttsAudioBytes = 0;
    let successFinalizing = false;
    let successFinalizeTimerId = null;
    let ttsWaitTimedOut = false;
    const ttsChunks = [];
    const eventWaiters = new Map();

    const ws = new WebSocket(DOUBAO_REALTIME_URL, { headers });

    const timeoutId = setTimeout(() => {
      if (waitStage === "asr_or_chat") {
        console.warn(
          `[doubao] timeout diagnostics connectionStarted=${connectionStarted} sessionStarted=${sessionStarted} receivedAnyServerFrame=${receivedAnyServerFrame} asrInfoReceived=${asrInfoReceived} asrResponseReceived=${asrResponseReceived} asrEnded=${asrEnded} chatResponseReceived=${chatResponseReceived} dialogCommonErrorReceived=${dialogCommonErrorReceived} endAsrSent=${endAsrSent}`
        );
      }

      finish({
        ok: false,
        error: transportError || getStageTimeoutError(waitStage)
      });
    }, SESSION_TIMEOUT_MS);

    ws.on("open", async () => {
      try {
        await sendWebSocketFrame(
          ws,
          buildClientEventFrame({
            eventId: CLIENT_EVENT.StartConnection,
            payload: {},
            connectId
          })
        );

        await waitForServerEvent({
          successEventId: 50,
          failureEventIds: [51],
          timeoutMs: CONNECTION_STARTED_TIMEOUT_MS,
          timeoutError: "DOUBAO_TIMEOUT_WAIT_CONNECTION_STARTED"
        });
        waitStage = "session_started";

        const startSessionFrame = buildClientEventFrame({
          eventId: CLIENT_EVENT.StartSession,
          payload: startSessionPayload,
          connectId,
          sessionId
        });
        assertFrameShape({
          frame: startSessionFrame,
          payloadLength: Buffer.byteLength(JSON.stringify(startSessionPayload), "utf8"),
          expectedFrameLength: startSessionFrame.length,
          expectedFlags: CLIENT_FLAGS_WITH_EVENT,
          expectedOptionalSize: 44,
          label: "StartSession"
        });
        await sendWebSocketFrame(ws, startSessionFrame);

        await waitForServerEvent({
          successEventId: 150,
          failureEventIds: [153],
          timeoutMs: SESSION_STARTED_TIMEOUT_MS,
          timeoutError: "DOUBAO_TIMEOUT_WAIT_SESSION_STARTED"
        });
        waitStage = "asr_or_chat";

        const audioChunks = splitBuffer(wav.pcmBuffer, AUDIO_PACKET_SIZE_BYTES);
        for (let index = 0; index < audioChunks.length; index += 1) {
          const isLast = index === audioChunks.length - 1;
          const audioChunk = audioChunks[index];
          const audioFrame = buildAudioFrame({
            eventId: CLIENT_EVENT.TaskRequest,
            audioChunk,
            sessionId,
            isLast
          });

          if (!isLast && audioChunk.length === AUDIO_PACKET_SIZE_BYTES) {
            assertFrameShape({
              frame: audioFrame,
              payloadLength: AUDIO_PACKET_SIZE_BYTES,
              expectedFrameLength: 692,
              expectedFlags: CLIENT_FLAGS_WITH_EVENT,
              expectedOptionalSize: 44,
              label: "TaskRequest"
            });
          }

          if (isLast) {
            assertFrameShape({
              frame: audioFrame,
              payloadLength: audioChunk.length,
              expectedFrameLength: 4 + 44 + 4 + audioChunk.length,
              expectedFlags: CLIENT_FLAGS_WITH_EVENT,
              expectedOptionalSize: 44,
              label: "TaskRequestLast"
            });
          }

          await sendWebSocketFrame(ws, audioFrame);

          if (!isLast) {
            await sleep(AUDIO_PACKET_INTERVAL_MS);
          }
        }

        finishedSendingAudio = true;
        console.log("[doubao] audio stream finished, sending EndASR=400");
        const endAsrFrame = buildClientEventFrame({
          eventId: CLIENT_EVENT.EndASR,
          payload: {},
          connectId,
          sessionId
        });
        assertFrameShape({
          frame: endAsrFrame,
          payloadLength: 2,
          expectedFrameLength: 4 + 44 + 4 + 2,
          expectedFlags: CLIENT_FLAGS_WITH_EVENT,
          expectedOptionalSize: 44,
          label: "EndASR"
        });
        await sendWebSocketFrame(ws, endAsrFrame);
        endAsrSent = true;
        console.log("[doubao] EndASR=400 sent, waiting for ASR/Chat events");
      } catch (error) {
        finish({
          ok: false,
          error: getErrorMessage(error)
        });
      }
    });

    ws.on("message", (rawData) => {
      try {
        const frame = parseServerFrame(toBuffer(rawData));
        receivedAnyServerFrame = true;
        if (frame.logId) {
          lastLogId = frame.logId;
        }

        logServerFrameSummary(frame);
        notifyEventWaiters(frame);

        if (frame.transportError) {
          transportError = frame.transportError;
          console.warn(`[doubao] transport error logId=${lastLogId || "n/a"} ${frame.transportError}`);
          finish({ ok: false, error: frame.transportError });
          return;
        }

        if (frame.eventId && !SERVER_EVENT_NAME[frame.eventId]) {
          console.log(`[doubao] server event id=${frame.eventId}`);
        }

        if (frame.eventName === "ConnectionFailed") {
          transportError = frame.errorMessage || "DOUBAO_CONNECTION_FAILED";
          finish({ ok: false, error: transportError });
          return;
        }

        if (frame.eventName === "ConnectionStarted") {
          connectionStarted = true;
          return;
        }

        if (frame.eventName === "SessionFailed") {
          transportError = frame.errorMessage || "DOUBAO_SESSION_FAILED";
          finish({ ok: false, error: transportError });
          return;
        }

        if (frame.eventName === "SessionStarted") {
          sessionStarted = true;
          return;
        }

        if (frame.eventName === "ASRInfo") {
          asrInfoReceived = true;
          return;
        }

        if (frame.eventName === "ASRResponse") {
          asrResponseReceived = true;
          const asrResult = extractAsrTexts(frame.payload);
          if (asrResult.interimText) {
            transcript = appendSegment(transcript, asrResult.interimText);
          }
          if (asrResult.finalText) {
            finalTranscript = appendSegment(finalTranscript, asrResult.finalText);
            transcript = finalTranscript;
          }
          if (transcript) {
            console.log(`[doubao] transcript=${transcript}`);
          }
          return;
        }

        if (frame.eventName === "ASREnded") {
          asrEnded = true;
          return;
        }

        if (frame.eventName === "ChatResponse") {
          chatResponseReceived = true;
          const text = extractReplyText(frame.payload);
          if (text) {
            replyText = appendSegment(replyText, text);
            console.log(`[doubao] replyText=${replyText}`);
          }
          return;
        }

        if (frame.eventName === "TTSSentenceStart") {
          const text = extractReplyText(frame.payload);
          if (text) {
            fallbackReplyText = appendSegment(fallbackReplyText, text);
          }
          return;
        }

        if (frame.eventName === "ChatEnded") {
          chatEnded = true;
          console.log("[doubao-tts] chatEnded=true, waiting for realtime tts...");
          scheduleSuccessFinalize();
          return;
        }

        if (frame.eventName === "TTSResponse" ||
            (frame.audio && frame.audio.length > 0 &&
             (frame.messageType === MESSAGE_TYPE.SERVER_AUDIO_ONLY_RESPONSE ||
              frame.messageType === MESSAGE_TYPE.SERVER_LEGACY_AUDIO_ONLY_RESPONSE))) {
          if (frame.audio && frame.audio.length > 0) {
            ttsChunks.push(frame.audio);
            ttsChunkCount += 1;
            ttsAudioBytes += frame.audio.length;
            console.log(`[doubao-tts] tts chunk received bytes=${frame.audio.length}`);
          }
          return;
        }

        if (frame.eventName === "TTSEnded") {
          ttsEnded = true;
          maybeSendFinishSession();
          scheduleSuccessFinalize(true);
          return;
        }

        if (frame.eventName === "DialogCommonError") {
          dialogCommonErrorReceived = true;
          transportError = frame.errorMessage || frame.eventName;
          finish({ ok: false, error: transportError });
        }
      } catch (error) {
        transportError = getErrorMessage(error);
        finish({ ok: false, error: transportError });
      }
    });

    ws.on("unexpected-response", (request, response) => {
      void request;
      const statusCode = response && response.statusCode ? response.statusCode : 0;
      const statusMessage = response && response.statusMessage ? response.statusMessage : "";
      const logId = response && response.headers
        ? toStringValue(response.headers["x-tt-logid"] || response.headers["x_tt_logid"])
        : "";

      console.warn(
        `[doubao] unexpected-response statusCode=${statusCode || "unknown"} statusMessage=${statusMessage || ""} x-tt-logid=${logId || "n/a"}`
      );

      if (statusCode === 403) {
        transportError = "doubao websocket handshake forbidden: check APP ID, Access Token, Resource ID, App Key, and service permission";
      } else {
        transportError = `doubao websocket unexpected response: ${statusCode || "unknown"} ${statusMessage}`.trim();
      }

      finish({ ok: false, error: transportError });
    });

    ws.on("error", (error) => {
      if (transportError) {
        finish({ ok: false, error: transportError });
        return;
      }

      transportError = getErrorMessage(error);
      finish({ ok: false, error: transportError });
    });

    ws.on("close", () => {
      if (settled) {
        return;
      }

      const resolvedTranscript = finalTranscript || transcript;
      const resolvedReplyText = replyText || fallbackReplyText;

      if (resolvedTranscript || resolvedReplyText) {
        scheduleSuccessFinalize(true);
        return;
      }

      finish({ ok: false, error: transportError || getStageTimeoutError(waitStage) || "DOUBAO_REALTIME_CLOSED" });
    });

    function finish(result) {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timeoutId);
      clearTimeout(successFinalizeTimerId);
      rejectPendingEventWaiters(transportError || getStageTimeoutError(waitStage) || "DOUBAO_REALTIME_CLOSED");

      try {
        if (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING) {
          ws.close();
        }
      } catch (error) {
        void error;
      }

      resolve(result);
    }

    function maybeSendFinishSession() {
      if (!finishedSendingAudio || finishSessionSent || ws.readyState !== WebSocket.OPEN) {
        return;
      }

      if (!ttsEnded && !ttsWaitTimedOut) {
        return;
      }

      finishSessionSent = true;
      console.log(`[doubao] sending FinishSession chatEnded=${chatEnded} ttsEnded=${ttsEnded} finishedSendingAudio=${finishedSendingAudio}`);
      sendWebSocketFrame(
        ws,
        buildClientEventFrame({
          eventId: CLIENT_EVENT.FinishSession,
          payload: {},
          connectId,
          sessionId
        })
      ).catch((error) => {
        transportError = getErrorMessage(error);
      });
    }

    function scheduleSuccessFinalize(forceImmediate = false) {
      if (settled || successFinalizing) {
        return;
      }

      if (!chatEnded && !forceImmediate) {
        return;
      }

      clearTimeout(successFinalizeTimerId);

      if (forceImmediate || ttsEnded) {
        finalizeSuccess();
        return;
      }

      successFinalizeTimerId = setTimeout(() => {
        ttsWaitTimedOut = true;
        console.warn(`[doubao-tts] tts wait timeout ms=${TTS_FINALIZATION_WAIT_MS}`);
        if (ttsAudioBytes <= 0) {
          console.warn("[doubao-tts] no tts chunks before return");
        }
        maybeSendFinishSession();
        finalizeSuccess();
      }, TTS_FINALIZATION_WAIT_MS);
    }

    async function finalizeSuccess() {
      if (settled || successFinalizing) {
        return;
      }

      successFinalizing = true;
      const resolvedTranscript = finalTranscript || transcript;
      const resolvedReplyText = replyText || fallbackReplyText;

      try {
        const result = await buildDialogueSuccess({
          transcript: resolvedTranscript,
          replyText: resolvedReplyText,
          ttsChunks,
          outputDir,
          publicBaseUrl,
          sessionId,
          ttsChunkCount,
          ttsAudioBytes,
          runtimeConfig,
          speaker: config.speaker
        });
        finish(result);
      } catch (error) {
        successFinalizing = false;
        transportError = getErrorMessage(error);
        if (resolvedTranscript || resolvedReplyText) {
          finish({
            ok: true,
            transcript: resolvedTranscript,
            replyText: resolvedReplyText,
            audioUrl: "",
            source: "doubao_partial",
            error: `TTS_NOT_AVAILABLE:${getShortErrorMessage(error)}`
          });
          return;
        }

        finish({ ok: false, error: transportError });
      }
    }

    function waitForServerEvent({ successEventId, failureEventIds = [], timeoutMs, timeoutError }) {
      return new Promise((resolveWait, rejectWait) => {
        const timerId = setTimeout(() => {
          eventWaiters.delete(successEventId);
          rejectWait(new Error(timeoutError));
        }, timeoutMs);

        eventWaiters.set(successEventId, {
          failureEventIds,
          resolve(frame) {
            clearTimeout(timerId);
            eventWaiters.delete(successEventId);
            resolveWait(frame);
          },
          reject(errorMessage) {
            clearTimeout(timerId);
            eventWaiters.delete(successEventId);
            rejectWait(new Error(errorMessage));
          }
        });
      });
    }

    function notifyEventWaiters(frame) {
      if (!frame || !frame.eventId) {
        return;
      }

      const directWaiter = eventWaiters.get(frame.eventId);
      if (directWaiter) {
        directWaiter.resolve(frame);
        return;
      }

      for (const waiter of eventWaiters.values()) {
        if (waiter.failureEventIds.includes(frame.eventId)) {
          waiter.reject(frame.errorMessage || getFailureEventError(frame.eventId));
          return;
        }
      }
    }

    function rejectPendingEventWaiters(errorMessage) {
      for (const waiter of eventWaiters.values()) {
        waiter.reject(errorMessage);
      }
      eventWaiters.clear();
    }
  });
}

async function readWavPcm16(audioPath) {
  const fileBuffer = await fs.promises.readFile(audioPath);

  if (fileBuffer.length < 44) {
    throw new Error("WAV_INVALID: file too small");
  }

  if (fileBuffer.toString("ascii", 0, 4) !== "RIFF" || fileBuffer.toString("ascii", 8, 12) !== "WAVE") {
    throw new Error("WAV_INVALID: expected RIFF/WAVE");
  }

  let offset = 12;
  let fmtChunk = null;
  let dataChunk = null;

  while (offset + 8 <= fileBuffer.length) {
    const chunkId = fileBuffer.toString("ascii", offset, offset + 4);
    const chunkSize = fileBuffer.readUInt32LE(offset + 4);
    const chunkDataStart = offset + 8;
    const chunkDataEnd = chunkDataStart + chunkSize;

    if (chunkDataEnd > fileBuffer.length) {
      throw new Error(`WAV_INVALID: chunk ${chunkId} exceeds file size`);
    }

    if (chunkId === "fmt ") {
      fmtChunk = fileBuffer.slice(chunkDataStart, chunkDataEnd);
    }

    if (chunkId === "data") {
      dataChunk = fileBuffer.slice(chunkDataStart, chunkDataEnd);
    }

    offset = chunkDataEnd + (chunkSize % 2);
  }

  if (!fmtChunk) {
    throw new Error("WAV_INVALID: missing fmt chunk");
  }

  if (!dataChunk) {
    throw new Error("WAV_INVALID: missing data chunk");
  }

  if (fmtChunk.length < 16) {
    throw new Error("WAV_INVALID: fmt chunk too small");
  }

  const audioFormat = fmtChunk.readUInt16LE(0);
  const numChannels = fmtChunk.readUInt16LE(2);
  const sampleRate = fmtChunk.readUInt32LE(4);
  const bitsPerSample = fmtChunk.readUInt16LE(14);

  if (audioFormat !== 1) {
    throw new Error(`WAV_UNSUPPORTED_FORMAT: audioFormat=${audioFormat}`);
  }

  if (numChannels !== 1) {
    throw new Error(`WAV_UNSUPPORTED_CHANNELS: numChannels=${numChannels}`);
  }

  if (sampleRate !== 16000) {
    throw new Error(`WAV_UNSUPPORTED_SAMPLE_RATE: sampleRate=${sampleRate}`);
  }

  if (bitsPerSample !== 16) {
    throw new Error(`WAV_UNSUPPORTED_BITS_PER_SAMPLE: bitsPerSample=${bitsPerSample}`);
  }

  const bytesPerSample = bitsPerSample / 8;
  const sampleCount = Math.floor(dataChunk.length / bytesPerSample / numChannels);
  const durationMs = Math.round((sampleCount / sampleRate) * 1000);

  return {
    pcmBuffer: dataChunk,
    sampleRate,
    channels: numChannels,
    bitsPerSample,
    durationMs,
    warnings: []
  };
}

function readDoubaoRealtimeConfig(runtimeConfig = {}) {
  return {
    appId: getEnv("DOUBAO_REALTIME_APP_ID") || DEFAULT_APP_ID,
    accessKey: toStringValue(runtimeConfig.accessKey) || getEnv("DOUBAO_REALTIME_ACCESS_KEY"),
    resourceId: getEnv("DOUBAO_REALTIME_RESOURCE_ID") || DEFAULT_RESOURCE_ID,
    appKey: DEFAULT_APP_KEY,
    model: getEnv("DOUBAO_REALTIME_MODEL") || DEFAULT_MODEL,
    speaker: getEnv("DOUBAO_REALTIME_SPEAKER") || DEFAULT_SPEAKER
  };
}

function isDoubaoCooldownActive() {
  return Date.now() < doubaoCooldownUntil;
}

function maybeEnableDoubaoCooldown(errorMessage) {
  const normalizedError = toStringValue(errorMessage).toLowerCase();
  if (!normalizedError || (!normalizedError.includes("quota exceeded") && !normalizedError.includes("qpm"))) {
    return;
  }

  doubaoCooldownUntil = Date.now() + DOUBAO_QPM_COOLDOWN_MS;
  console.warn("[doubao] qpm quota exceeded, cooldown enabled for 60s");
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
    session: {
      id: sessionId
    },
    asr: {
      audio_info: {
        format: "pcm",
        sample_rate: 16000,
        channel: 1
      },
      extra: {}
    },
    tts: {
      speaker: config.speaker,
      audio_config: {
        channel: 1,
        format: "pcm_s16le",
        sample_rate: 24000
      },
      extra: {}
    },
    dialog: {
      bot_name: roleName || roleId || "Mozart",
      system_role: DEFAULT_SYSTEM_ROLE,
      speaking_style: DEFAULT_SPEAKING_STYLE,
      extra: {
        input_mod: "push_to_talk",
        model: config.model,
        role_id: roleId || "",
        scene_id: sceneId || ""
      }
    }
  };
}

function buildClientEventFrame({ eventId, payload, connectId, sessionId }) {
  const payloadObject = payload && typeof payload === "object" && !Buffer.isBuffer(payload)
    ? payload
    : {};
  const payloadBuffer = Buffer.from(JSON.stringify(payloadObject), "utf8");
  const optionalBuffers = [buildUInt32Buffer(eventId)];

  if (sessionId) {
    optionalBuffers.push(buildSizedStringBuffer(sessionId));
  }

  const frame = buildFrame({
    messageType: MESSAGE_TYPE.CLIENT_FULL_REQUEST,
    flags: CLIENT_FLAGS_WITH_EVENT,
    serialization: SERIALIZATION.JSON,
    compression: COMPRESSION.NONE,
    payloadBuffer,
    optionalBuffers,
    debugLabel: `client-event:${eventId}`,
    debugMeta: {
      eventId,
      connectIdLength: connectId ? connectId.length : 0,
      sessionIdLength: sessionId ? sessionId.length : 0
    }
  });

  if (eventId === CLIENT_EVENT.StartConnection) {
    assertFrameShape({
      frame,
      payloadLength: payloadBuffer.length,
      expectedFrameLength: 14,
      expectedFlags: CLIENT_FLAGS_WITH_EVENT,
      expectedOptionalSize: 4,
      label: "StartConnection"
    });

    const expectedFrame = Buffer.from([17, 20, 16, 0, 0, 0, 0, 1, 0, 0, 0, 2, 123, 125]);
    if (!frame.equals(expectedFrame)) {
      throw new Error("StartConnection bytes do not match documented example");
    }
  }

  return frame;
}

function buildAudioFrame({ eventId, audioChunk, sessionId, isLast }) {
  const optionalBuffers = [
    buildUInt32Buffer(eventId),
    buildSizedStringBuffer(sessionId)
  ];
  const frame = buildFrame({
    messageType: MESSAGE_TYPE.CLIENT_AUDIO_ONLY_REQUEST,
    flags: CLIENT_FLAGS_WITH_EVENT,
    serialization: SERIALIZATION.NONE,
    compression: COMPRESSION.NONE,
    payloadBuffer: Buffer.from(audioChunk),
    optionalBuffers,
    debugLabel: `audio-event:${eventId}`,
    debugMeta: {
      eventId,
      isLast,
      sessionIdLength: sessionId ? sessionId.length : 0
    }
  });

  return frame;
}

function parseServerFrame(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length < 4) {
    throw new Error("DOUBAO_INVALID_FRAME");
  }

  const headerByte1 = buffer[0];
  const headerByte2 = buffer[1];
  const headerByte3 = buffer[2];
  const headerSize = (headerByte1 & 0x0f) * 4;
  const messageType = (headerByte2 & 0xf0) >> 4;
  const flags = headerByte2 & 0x0f;
  const serialization = (headerByte3 & 0xf0) >> 4;
  const compression = headerByte3 & 0x0f;

  if (buffer.length < headerSize) {
    throw new Error("DOUBAO_INVALID_FRAME_HEADER");
  }

  const body = buffer.slice(headerSize);
  const payloadSection = parsePayloadSection(body, messageType, flags);
  const payloadBuffer = maybeDecompressPayload(payloadSection.payloadBuffer, compression);
  const frame = {
    messageType,
    flags,
    serialization,
    compression,
    sequence: payloadSection.sequence,
    optionalSize: payloadSection.optionalSize,
    sessionId: payloadSection.sessionId,
    audio: null,
    payload: null,
    eventId: payloadSection.eventId,
    eventName: SERVER_EVENT_NAME[payloadSection.eventId] || "",
    logId: "",
    errorMessage: "",
    transportError: "",
    payloadLength: payloadBuffer.length,
    parseMode: payloadSection.parseMode || "unknown"
  };

  if (messageType === MESSAGE_TYPE.SERVER_AUDIO_ONLY_RESPONSE ||
      messageType === MESSAGE_TYPE.SERVER_LEGACY_AUDIO_ONLY_RESPONSE) {
    frame.audio = payloadBuffer;
    return frame;
  }

  if (serialization === SERIALIZATION.JSON) {
    frame.payload = parseJsonSafe(payloadBuffer);
    frame.eventId = frame.eventId || extractEventId(frame.payload);
    frame.eventName = SERVER_EVENT_NAME[frame.eventId] || extractEventName(frame.payload);
    frame.logId = extractLogId(frame.payload);
    frame.errorMessage = extractErrorMessage(frame.payload);
  } else {
    frame.payload = payloadBuffer;
    if (messageType === MESSAGE_TYPE.SERVER_FULL_RESPONSE || messageType === MESSAGE_TYPE.SERVER_ERROR_RESPONSE) {
      const parsedJsonPayload = parseJsonSafe(payloadBuffer);
      if (!parsedJsonPayload.parseError) {
        frame.payload = parsedJsonPayload;
        frame.eventId = frame.eventId || extractEventId(parsedJsonPayload);
        frame.eventName = SERVER_EVENT_NAME[frame.eventId] || extractEventName(parsedJsonPayload);
        frame.logId = extractLogId(parsedJsonPayload);
        frame.errorMessage = extractErrorMessage(parsedJsonPayload);
      }
    }

    if (!frame.errorMessage && messageType === MESSAGE_TYPE.SERVER_ERROR_RESPONSE) {
      frame.errorMessage = bufferToTrimmedUtf8(payloadBuffer);
    }
  }

  if (messageType === MESSAGE_TYPE.SERVER_ERROR_RESPONSE) {
    frame.transportError = frame.errorMessage || bufferToTrimmedUtf8(payloadBuffer) || "DOUBAO_SERVER_ERROR";
  }

  return frame;
}

function parsePayloadSection(body, messageType, flags) {
  if (body.length === 0) {
    return {
      sequence: null,
      eventId: 0,
      sessionId: "",
      optionalSize: 0,
      payloadBuffer: Buffer.alloc(0),
      parseMode: "empty"
    };
  }

  if (flags === CLIENT_FLAGS_WITH_EVENT) {
    return parseEventPayloadSection(body);
  }

  if ((messageType === MESSAGE_TYPE.CLIENT_AUDIO_ONLY_REQUEST ||
      messageType === MESSAGE_TYPE.SERVER_AUDIO_ONLY_RESPONSE ||
      messageType === MESSAGE_TYPE.SERVER_LEGACY_AUDIO_ONLY_RESPONSE ||
      messageType === MESSAGE_TYPE.SERVER_ACK) && body.length >= 8) {
    const sequence = body.readInt32BE(0);
    const payloadSize = body.readUInt32BE(4);
    if (payloadSize >= 0 && 8 + payloadSize <= body.length) {
      return {
        sequence,
        eventId: 0,
        sessionId: "",
        optionalSize: 4,
        payloadBuffer: body.slice(8, 8 + payloadSize),
        parseMode: "sequence_payload"
      };
    }
  }

  if (body.length >= 8) {
    const possibleSequence = body.readInt32BE(0);
    const payloadSize = body.readUInt32BE(4);
    if (payloadSize >= 0 && 8 + payloadSize <= body.length && Math.abs(possibleSequence) < 1000000000) {
      return {
        sequence: possibleSequence,
        eventId: 0,
        sessionId: "",
        optionalSize: 4,
        payloadBuffer: body.slice(8, 8 + payloadSize),
        parseMode: "sequence_payload_fallback"
      };
    }
  }

  if (body.length >= 4) {
    const payloadSize = body.readUInt32BE(0);
    if (payloadSize >= 0 && 4 + payloadSize <= body.length) {
      return {
        sequence: null,
        eventId: 0,
        sessionId: "",
        optionalSize: 0,
        payloadBuffer: body.slice(4, 4 + payloadSize),
        parseMode: "payload_only"
      };
    }
  }

  return {
    sequence: null,
    eventId: 0,
    sessionId: "",
    optionalSize: 0,
    payloadBuffer: body,
    parseMode: "raw_body"
  };
}

function parseEventPayloadSection(body) {
  if (body.length < 8) {
    return {
      sequence: null,
      eventId: body.length >= 4 ? body.readUInt32BE(0) : 0,
      sessionId: "",
      optionalSize: body.length,
      payloadBuffer: Buffer.alloc(0),
      parseMode: "event_short"
    };
  }

  let offset = 0;
  const eventId = body.readUInt32BE(offset);
  offset += 4;

  let sessionId = "";
  let payloadSizeOffset = offset;
  let parseMode = "event_payload";

  if (isSessionScopedEvent(eventId) && body.length >= offset + 4) {
    const sessionIdSize = body.readUInt32BE(offset);
    const sessionIdStart = offset + 4;
    const sessionIdEnd = sessionIdStart + sessionIdSize;
    if (sessionIdEnd + 4 <= body.length) {
      sessionId = body.slice(sessionIdStart, sessionIdEnd).toString("utf8");
      payloadSizeOffset = sessionIdEnd;
      parseMode = "event_session_payload";
    }
  }

  if (body.length < payloadSizeOffset + 4) {
    return {
      sequence: null,
      eventId,
      sessionId,
      optionalSize: payloadSizeOffset,
      payloadBuffer: Buffer.alloc(0),
      parseMode
    };
  }

  const payloadSize = body.readUInt32BE(payloadSizeOffset);
  const payloadStart = payloadSizeOffset + 4;
  const payloadEnd = payloadStart + payloadSize;
  const boundedPayloadEnd = payloadEnd <= body.length ? payloadEnd : body.length;

  return {
    sequence: null,
    eventId,
    sessionId,
    optionalSize: payloadSizeOffset,
    payloadBuffer: body.slice(payloadStart, boundedPayloadEnd),
    parseMode
  };
}

function buildFrame({
  messageType,
  flags,
  serialization,
  compression,
  payloadBuffer,
  optionalBuffers = [],
  debugLabel = "frame",
  debugMeta = {}
}) {
  const headerSizeWords = 1;
  const headerSize = headerSizeWords * 4;
  const header = Buffer.from([
    (0x1 << 4) | headerSizeWords,
    ((messageType & 0x0f) << 4) | (flags & 0x0f),
    ((serialization & 0x0f) << 4) | (compression & 0x0f),
    0x00
  ]);
  const normalizedPayloadBuffer = Buffer.isBuffer(payloadBuffer)
    ? payloadBuffer
    : Buffer.from(payloadBuffer || "");
  const normalizedOptionalBuffers = Array.isArray(optionalBuffers)
    ? optionalBuffers.filter((buffer) => Buffer.isBuffer(buffer) && buffer.length > 0)
    : [];
  const optionalSize = normalizedOptionalBuffers.reduce((sum, buffer) => sum + buffer.length, 0);
  const payloadLength = normalizedPayloadBuffer.length;
  const payloadSizeBuffer = Buffer.alloc(4);
  payloadSizeBuffer.writeUInt32BE(payloadLength, 0);
  const frame = Buffer.concat([header, ...normalizedOptionalBuffers, payloadSizeBuffer, normalizedPayloadBuffer]);
  const expectedFrameLength = headerSize + optionalSize + 4 + payloadLength;

  if (payloadLength !== normalizedPayloadBuffer.length) {
    throw new Error(`${debugLabel} payload length mismatch`);
  }

  if (frame.length !== expectedFrameLength) {
    throw new Error(`${debugLabel} frame length mismatch: expected=${expectedFrameLength} actual=${frame.length}`);
  }

  console.log(
    `[doubao] ${debugLabel} messageType=${messageType} flags=${flags} payloadLength=${payloadLength} frameLength=${frame.length} optionalSize=${optionalSize}` +
    formatFrameDebugMeta(debugMeta)
  );

  return frame;
}

async function buildDialogueSuccess({ transcript, replyText, ttsChunks, outputDir, publicBaseUrl, sessionId, ttsChunkCount, ttsAudioBytes, runtimeConfig, speaker }) {
  const normalizedTranscript = toStringValue(transcript);
  const normalizedReplyText = toStringValue(replyText);

  if (!normalizedTranscript && !normalizedReplyText) {
    return {
      ok: false,
      error: "DOUBAO_REALTIME_EMPTY_DIALOGUE"
    };
  }

  const effectiveReplyText = normalizedReplyText || (normalizedTranscript ? "我听到了。先别急，我们先把线索理清。" : "");
  const replyFallbackUsed = !normalizedReplyText && Boolean(normalizedTranscript);

  if (!effectiveReplyText) {
    return {
      ok: false,
      error: "DOUBAO_REALTIME_REPLY_EMPTY"
    };
  }

  console.log(`[doubao-tts] tts chunks count=${ttsChunkCount || 0}`);
  console.log(`[doubao-tts] tts total bytes=${ttsAudioBytes || 0}`);

  let audioUrl = "";
  let postTtsError = "";
  if (Array.isArray(ttsChunks) && ttsChunks.length > 0 && ttsAudioBytes > 0 && outputDir && publicBaseUrl) {
    const savedAudio = await saveDoubaoTtsWav({ outputDir, publicBaseUrl, sessionId, ttsChunks });
    audioUrl = savedAudio.audioUrl;
    console.log(`[doubao-tts] writing realtime wav path=${savedAudio.filePath}`);
    console.log(`[doubao-tts] realtime wav bytes=${savedAudio.bytes}`);
    console.log(`[doubao-tts] realtime audioUrl=${audioUrl}`);
    console.log("[doubao-post-tts] skipped because realtime tts audio exists");
  } else if (!HTTP_TTS_ENABLED) {
    postTtsError = "REALTIME_TTS_TIMEOUT";
    console.log("[doubao-post-tts] skipped because DOUBAO_HTTP_TTS_ENABLED is not true");
  } else {
    try {
      const savedAudio = await createDoubaoTtsAudio({
        text: effectiveReplyText,
        outputDir,
        publicBaseUrl,
        speaker,
        runtimeConfig
      });
      audioUrl = savedAudio.audioUrl;
      console.log(`[doubao-post-tts] generated bytes=${savedAudio.bytes}`);
    } catch (error) {
      postTtsError = getShortErrorMessage(error);
      console.warn(`[doubao-post-tts] failed: ${postTtsError}`);
    }
  }

  if (replyFallbackUsed) {
    return {
      ok: true,
      transcript: normalizedTranscript,
      replyText: effectiveReplyText,
      audioUrl,
      source: audioUrl ? "doubao" : "doubao_partial",
      error: "DOUBAO_REPLY_FALLBACK"
    };
  }

  if (!audioUrl) {
    return {
      ok: true,
      transcript: normalizedTranscript,
      replyText: effectiveReplyText,
      audioUrl: "",
      source: "doubao_partial",
      error: postTtsError ? `TTS_NOT_AVAILABLE:${postTtsError}` : "TTS_NOT_AVAILABLE"
    };
  }

  return {
    ok: true,
    transcript: normalizedTranscript,
    replyText: effectiveReplyText,
    audioUrl,
    source: "doubao",
    error: ""
  };
}

function createFailureResponse(error) {
  return {
    ok: false,
    transcript: "",
    replyText: "豆包实时语音暂时不可用，已切换到后备流程。",
    audioUrl: "",
    speakingVideoNodeId: "role_speaking",
    emotion: "calm",
    source: "doubao_error",
    error: toStringValue(error) || "DOUBAO_REALTIME_FAILED"
  };
}

function appendSegment(current, next) {
  if (!next) {
    return current;
  }

  if (!current) {
    return next;
  }

  if (current.includes(next)) {
    return current;
  }

  if (next.includes(current)) {
    return next;
  }

  return `${current}${next}`;
}

function extractAsrTexts(payload) {
  const results = Array.isArray(payload && payload.results) ? payload.results : [];
  let interimText = "";
  let finalText = "";

  for (const item of results) {
    const text = firstNonEmptyString([
      toStringValue(item && item.text),
      toStringValue(item && item.utterance)
    ]);

    if (!text) {
      continue;
    }

    if (item && item.is_interim === false) {
      finalText = appendSegment(finalText, text);
    } else {
      interimText = appendSegment(interimText, text);
    }
  }

  if (!interimText && !finalText) {
    const fallbackText = extractTranscriptText(payload);
    if (fallbackText) {
      finalText = fallbackText;
    }
  }

  return { interimText, finalText };
}

function extractTranscriptText(payload) {
  return firstNonEmptyString([
    deepFindString(payload, ["text"]),
    deepFindString(payload, ["transcript"]),
    deepFindString(payload, ["utterance"]),
    deepFindString(payload, ["result", "text"]),
    deepFindString(payload, ["payload", "text"]),
    deepFindString(payload, ["payload_msg", "message", "text"]),
    deepFindString(payload, ["message", "text"]),
    deepFindString(payload, ["data", "text"])
  ]);
}

function extractReplyText(payload) {
  return firstNonEmptyString([
    deepFindString(payload, ["replyText"]),
    deepFindString(payload, ["reply_text"]),
    deepFindString(payload, ["text"]),
    deepFindString(payload, ["message"]),
    deepFindString(payload, ["content"]),
    deepFindString(payload, ["payload_msg", "message", "text"]),
    deepFindString(payload, ["data", "text"]),
    deepFindString(payload, ["result", "text"])
  ]);
}

function extractEventId(payload) {
  if (!payload || typeof payload !== "object") {
    return 0;
  }

  const candidates = [
    payload.event_id,
    payload.eventId,
    payload.event,
    payload.header && payload.header.event,
    payload.payload_msg && payload.payload_msg.event_id,
    payload.payload_msg && payload.payload_msg.event
  ];

  for (const candidate of candidates) {
    const numeric = Number(candidate);
    if (Number.isFinite(numeric) && numeric > 0) {
      return numeric;
    }
  }

  return 0;
}

function extractEventName(payload) {
  if (!payload || typeof payload !== "object") {
    return "";
  }

  return firstNonEmptyString([
    toStringValue(payload.event_name),
    toStringValue(payload.eventName),
    toStringValue(payload.name),
    toStringValue(payload.type)
  ]);
}

function extractLogId(payload) {
  if (!payload || typeof payload !== "object") {
    return "";
  }

  return firstNonEmptyString([
    deepFindString(payload, ["x-tt-logid"]),
    deepFindString(payload, ["x_tt_logid"]),
    deepFindString(payload, ["logid"]),
    deepFindString(payload, ["log_id"])
  ]);
}

function extractErrorMessage(payload) {
  if (!payload || typeof payload !== "object") {
    return "";
  }

  const errorValue = payload.error;
  const structuredError = errorValue && typeof errorValue === "object"
    ? firstNonEmptyString([
      toStringValue(errorValue.message),
      toStringValue(errorValue.msg),
      toStringValue(errorValue.error_msg),
      toStringValue(errorValue.code)
    ])
    : "";

  return firstNonEmptyString([
    typeof errorValue === "string" ? errorValue : "",
    structuredError,
    deepFindString(payload, ["message"]),
    deepFindString(payload, ["msg"]),
    deepFindString(payload, ["error_msg"]),
    deepFindString(payload, ["payload_msg", "message"])
  ]);
}

function deepFindString(value, pathParts) {
  let current = value;
  for (const part of pathParts) {
    if (!current || typeof current !== "object") {
      return "";
    }
    current = current[part];
  }

  if (typeof current === "string") {
    return current.trim();
  }

  return "";
}

function firstNonEmptyString(values) {
  for (const value of values) {
    if (typeof value === "string" && value.trim()) {
      return value.trim();
    }
  }

  return "";
}

function parseJsonSafe(buffer) {
  const text = buffer.toString("utf8").trim();
  if (!text) {
    return {};
  }

  try {
    return JSON.parse(text);
  } catch (error) {
    return {
      text,
      parseError: getErrorMessage(error)
    };
  }
}

function maybeDecompressPayload(payloadBuffer, compression) {
  if (compression !== COMPRESSION.GZIP || payloadBuffer.length === 0) {
    return payloadBuffer;
  }

  try {
    return zlib.gunzipSync(payloadBuffer);
  } catch (error) {
    throw new Error(`DOUBAO_GZIP_DECOMPRESS_FAILED: ${getErrorMessage(error)}`);
  }
}

function gzipJson(value) {
  const jsonBuffer = Buffer.from(JSON.stringify(value), "utf8");
  return zlib.gzipSync(jsonBuffer);
}

async function saveDoubaoTtsWav({ outputDir, publicBaseUrl, sessionId, ttsChunks }) {
  const pcmBuffer = Buffer.concat(ttsChunks);
  if (pcmBuffer.length === 0) {
    throw new Error("DOUBAO_TTS_AUDIO_EMPTY");
  }

  await fs.promises.mkdir(outputDir, { recursive: true });
  const fileName = buildDoubaoTtsFileName(sessionId);
  const outputPath = path.join(outputDir, fileName);
  await writePcm16WavFile(outputPath, pcmBuffer, TTS_SAMPLE_RATE, TTS_CHANNELS);

  return {
    fileName,
    filePath: outputPath,
    audioUrl: buildGeneratedAudioUrl(publicBaseUrl, fileName),
    bytes: pcmBuffer.length
  };
}

async function writePcm16WavFile(outputPath, pcmBuffer, sampleRate = TTS_SAMPLE_RATE, channels = TTS_CHANNELS) {
  const normalizedPcmBuffer = Buffer.isBuffer(pcmBuffer) ? pcmBuffer : Buffer.from(pcmBuffer || []);
  const bitsPerSample = TTS_BITS_PER_SAMPLE;
  const byteRate = sampleRate * channels * (bitsPerSample / 8);
  const blockAlign = channels * (bitsPerSample / 8);
  const dataSize = normalizedPcmBuffer.length;
  const header = Buffer.alloc(44);

  header.write("RIFF", 0, "ascii");
  header.writeUInt32LE(36 + dataSize, 4);
  header.write("WAVE", 8, "ascii");
  header.write("fmt ", 12, "ascii");
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(channels, 22);
  header.writeUInt32LE(sampleRate, 24);
  header.writeUInt32LE(byteRate, 28);
  header.writeUInt16LE(blockAlign, 32);
  header.writeUInt16LE(bitsPerSample, 34);
  header.write("data", 36, "ascii");
  header.writeUInt32LE(dataSize, 40);

  const wavBuffer = Buffer.concat([header, normalizedPcmBuffer]);
  if (wavBuffer.length <= 44) {
    throw new Error("DOUBAO_TTS_WAV_INVALID");
  }

  await fs.promises.writeFile(outputPath, wavBuffer);
}

function buildDoubaoTtsFileName(sessionId) {
  const normalizedSessionId = toStringValue(sessionId).replace(/[^a-zA-Z0-9-_]/g, "");
  const suffix = normalizedSessionId || crypto.randomUUID();
  return `doubao-tts-${suffix}.wav`;
}

function buildGeneratedAudioUrl(publicBaseUrl, fileName) {
  return `${toStringValue(publicBaseUrl).replace(/\/$/, "")}/generated-audio/${fileName}`;
}

function splitBuffer(buffer, chunkSize) {
  const chunks = [];
  for (let offset = 0; offset < buffer.length; offset += chunkSize) {
    chunks.push(buffer.slice(offset, Math.min(offset + chunkSize, buffer.length)));
  }
  return chunks.length > 0 ? chunks : [Buffer.alloc(0)];
}

function buildUInt32Buffer(value) {
  const buffer = Buffer.alloc(4);
  buffer.writeUInt32BE(value >>> 0, 0);
  return buffer;
}

function buildSizedStringBuffer(value) {
  const stringBuffer = Buffer.from(toStringValue(value), "utf8");
  return Buffer.concat([buildUInt32Buffer(stringBuffer.length), stringBuffer]);
}

function assertFrameShape({ frame, payloadLength, expectedFrameLength, expectedFlags, expectedOptionalSize, label }) {
  const flags = frame[1] & 0x0f;
  const actualPayloadLength = frame.readUInt32BE(4 + expectedOptionalSize);
  const actualOptionalSize = frame.length - 4 - 4 - actualPayloadLength;

  if (frame.length !== expectedFrameLength) {
    throw new Error(`${label} frameLength mismatch: expected=${expectedFrameLength} actual=${frame.length}`);
  }

  if (flags !== expectedFlags) {
    throw new Error(`${label} flags mismatch: expected=${expectedFlags} actual=${flags}`);
  }

  if (actualOptionalSize !== expectedOptionalSize) {
    throw new Error(`${label} optionalSize mismatch: expected=${expectedOptionalSize} actual=${actualOptionalSize}`);
  }

  if (actualPayloadLength !== payloadLength) {
    throw new Error(`${label} payloadLength mismatch: expected=${payloadLength} actual=${actualPayloadLength}`);
  }
}

function formatFrameDebugMeta(debugMeta) {
  const fields = [];

  for (const [key, value] of Object.entries(debugMeta || {})) {
    if (value === undefined || value === null || value === "") {
      continue;
    }

    fields.push(`${key}=${value}`);
  }

  return fields.length > 0 ? ` ${fields.join(" ")}` : "";
}

function logServerFrameSummary(frame) {
  console.log(
    `[doubao] server-frame messageType=${frame.messageType} flags=${frame.flags} serialization=${frame.serialization} compression=${frame.compression} eventId=${frame.eventId || 0} optionalSize=${frame.optionalSize || 0} payloadLength=${frame.payloadLength || 0}`
  );
}

function isSessionScopedEvent(eventId) {
  return new Set([150, 152, 153, 350, 351, 352, 359, 450, 451, 459, 550, 559, 599]).has(eventId);
}

function getFailureEventError(eventId) {
  if (eventId === 51) {
    return "DOUBAO_CONNECTION_FAILED";
  }

  if (eventId === 153) {
    return "DOUBAO_SESSION_FAILED";
  }

  return SERVER_EVENT_NAME[eventId] || `DOUBAO_EVENT_${eventId}_FAILED`;
}

function bufferToTrimmedUtf8(buffer) {
  return Buffer.isBuffer(buffer) ? buffer.toString("utf8").trim() : "";
}

function getStageTimeoutError(waitStage) {
  if (waitStage === "connection_started") {
    return "DOUBAO_TIMEOUT_WAIT_CONNECTION_STARTED";
  }

  if (waitStage === "session_started") {
    return "DOUBAO_TIMEOUT_WAIT_SESSION_STARTED";
  }

  return "DOUBAO_TIMEOUT_WAIT_ASR_OR_CHAT";
}

function getAudioFrameDebugSummary(frame) {
  return {
    flags: frame[1] & 0x0f,
    optionalSize: frame.length - 4 - 4 - frame.readUInt32BE(48),
    payloadLength: frame.readUInt32BE(48),
    frameLength: frame.length
  };
}

function sendWebSocketFrame(ws, frame) {
  return new Promise((resolve, reject) => {
    ws.send(frame, (error) => {
      if (error) {
        reject(error);
        return;
      }

      resolve();
    });
  });
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function toBuffer(value) {
  if (Buffer.isBuffer(value)) {
    return value;
  }

  if (Array.isArray(value)) {
    return Buffer.concat(value.map((part) => toBuffer(part)));
  }

  if (value instanceof ArrayBuffer) {
    return Buffer.from(value);
  }

  if (ArrayBuffer.isView(value)) {
    return Buffer.from(value.buffer, value.byteOffset, value.byteLength);
  }

  return Buffer.from(value || "");
}

function toStringValue(value) {
  return typeof value === "string" ? value.trim() : "";
}

function getEnv(name) {
  return typeof process.env[name] === "string" ? process.env[name].trim() : "";
}

function getErrorMessage(error) {
  if (!error) {
    return "unknown error";
  }

  return error.stack || error.message || String(error);
}

function getShortErrorMessage(error) {
  if (!error) {
    return "unknown error";
  }

  return error.message || String(error);
}

module.exports = {
  createDoubaoVoiceDialogue,
  readWavPcm16,
  buildClientEventFrame,
  buildAudioFrame,
  parseServerFrame,
  runDoubaoRealtimeSession,
  getAudioFrameDebugSummary
};

const express = require("express");
const cors = require("cors");
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const multer = require("multer");
const OpenAI = require("openai");
require("dotenv").config();

const app = express();
const port = Number(process.env.PORT || 3000);
const model = process.env.OPENAI_MODEL || "gpt-4.1-mini";
const arkApiKey = process.env.ARK_API_KEY || "";
const arkBaseUrl = process.env.ARK_BASE_URL || "https://ark.cn-beijing.volces.com/api/v3";
const arkModel = process.env.ARK_MODEL || "doubao-seed-2-0-pro-260215";
const arkReasoningEffort = process.env.ARK_REASONING_EFFORT || "medium";
const transcriptionModel = process.env.OPENAI_TRANSCRIPTION_MODEL || "gpt-4o-mini-transcribe";
const ttsModel = process.env.OPENAI_TTS_MODEL || "gpt-4o-mini-tts";
const ttsVoice = process.env.OPENAI_TTS_VOICE || "alloy";
const publicBaseUrl = process.env.PUBLIC_BASE_URL || `http://localhost:${port}`;
const uploadDir = path.join(__dirname, "uploads");
const generatedAudioDir = path.join(__dirname, "public", "generated-audio");

fs.mkdirSync(uploadDir, { recursive: true });
fs.mkdirSync(generatedAudioDir, { recursive: true });

const upload = multer({
  dest: uploadDir,
  limits: {
    fileSize: 20 * 1024 * 1024
  }
});

const ROLE_START_NODE = {
  Mozart: "intro_Mozart",
  investigator: "intro_Mozart",
  protector: "intro_protector",
  survivor: "intro_survivor"
};

const DEFAULT_ROLE = {
  roleId: "Mozart",
  roleName: "Mozart",
  roleDescription: "冷静观察线索，适合从调查者入口进入剧情。",
  startNodeId: "intro_Mozart",
  traits: ["observe", "logic", "risk_control"]
};

const ROLE_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["roleId", "roleName", "roleDescription", "startNodeId", "traits"],
  properties: {
    roleId: {
      type: "string",
      enum: ["Mozart", "investigator", "protector", "survivor"]
    },
    roleName: {
      type: "string"
    },
    roleDescription: {
      type: "string"
    },
    startNodeId: {
      type: "string",
      enum: ["intro_Mozart", "intro_protector", "intro_survivor"]
    },
    traits: {
      type: "array",
      items: {
        type: "string"
      }
    }
  }
};

const openaiApiKey = normalizeEnvValue(process.env.OPENAI_API_KEY);
const openai = isLikelyApiKey(openaiApiKey)
  ? new OpenAI({ apiKey: openaiApiKey })
  : null;

app.use(cors());
app.use(express.json({ limit: "256kb" }));
app.use("/generated-audio", express.static(generatedAudioDir));

app.get("/health", (req, res) => {
  res.json({
    ok: true,
    service: "fmv-demo-server",
    arkConfigured: Boolean(arkApiKey),
    openaiConfigured: Boolean(openai)
  });
});

app.post("/api/assign-role", async (req, res) => {
  const payload = normalizePayload(req.body);

  console.log(
    `[assign-role] playerId=${payload.playerId || "unknown"}, answers=${payload.answers.length}`
  );

  try {
    const aiRole = await assignRoleWithAI(payload);
    const role = normalizeRole(aiRole);

    console.log(`[assign-role] return roleId=${role.roleId}, startNodeId=${role.startNodeId}`);
    res.json(role);
  } catch (error) {
    console.error("[assign-role] failed, return default role:", getErrorMessage(error));
    res.json(DEFAULT_ROLE);
  }
});

app.post("/api/voice-dialogue", upload.single("audio"), async (req, res) => {
  const uploadedFile = req.file;

  try {
    if (!uploadedFile) {
      throw new Error("audio file is required");
    }

    const roleId = toStringValue(req.body.roleId) || DEFAULT_ROLE.roleId;
    const roleName = toStringValue(req.body.roleName) || DEFAULT_ROLE.roleName;
    const sceneId = toStringValue(req.body.sceneId) || "dialogue_scene";

    console.log(
      `[voice-dialogue] roleId=${roleId}, sceneId=${sceneId}, file=${uploadedFile.originalname}, size=${uploadedFile.size}`
    );

    const result = await createVoiceDialogue({
      audioPath: uploadedFile.path,
      roleId,
      roleName,
      sceneId
    });

    res.json(result);
  } catch (error) {
    console.error("[voice-dialogue] failed:", getErrorMessage(error));
    res.json(createDefaultDialogueResponse(getErrorMessage(error)));
  } finally {
    if (uploadedFile) {
      fs.promises.unlink(uploadedFile.path).catch(() => {});
    }
  }
});

app.use((error, req, res, next) => {
  console.error("[server] request failed:", getErrorMessage(error));
  res.json(DEFAULT_ROLE);
});

async function assignRoleWithAI(payload) {
  console.log("[assign-role] fixed role mode, return Mozart.");
  return DEFAULT_ROLE;

  if (arkApiKey) {
    return assignRoleWithArk(payload);
  }

  if (!openai) {
    console.warn("[assign-role] OPENAI_API_KEY is not set, return default role.");
    return DEFAULT_ROLE;
  }

  if (payload.answers.length === 0) {
    console.warn("[assign-role] answers is empty, return default role.");
    return DEFAULT_ROLE;
  }

  const response = await openai.responses.create({
    model,
    input: [
      {
        role: "system",
        content: [
          "你是一个互动影像游戏的角色分配器。",
          "你必须根据玩家四道开场问题的选择，分配且只分配一个角色。",
          "只能返回符合 JSON Schema 的 JSON，不要返回 Markdown、解释、自然语言前后缀。",
          "角色映射必须严格遵守：Mozart -> intro_Mozart，investigator -> intro_Mozart，protector -> intro_protector，survivor -> intro_survivor。"
        ].join("\n")
      },
      {
        role: "user",
        content: JSON.stringify(payload)
      }
    ],
    text: {
      format: {
        type: "json_schema",
        name: "role_assignment",
        strict: true,
        schema: ROLE_SCHEMA
      }
    }
  });

  if (!response.output_text) {
    throw new Error("OpenAI response.output_text is empty");
  }

  return JSON.parse(response.output_text);
}

async function assignRoleWithArk(payload) {
  if (payload.answers.length === 0) {
    console.warn("[assign-role] answers is empty, return default role.");
    return DEFAULT_ROLE;
  }

  const content = [
    "你是一个互动影像游戏的角色分配器。",
    "你必须根据玩家四道开场问题的选择，分配且只分配一个角色。",
    "只能返回 JSON，不要返回 Markdown、解释、自然语言前后缀。",
    "JSON 字段必须是 roleId, roleName, roleDescription, startNodeId, traits。",
    "角色映射必须严格遵守：Mozart -> intro_Mozart，investigator -> intro_Mozart，protector -> intro_protector，survivor -> intro_survivor。",
    "玩家答案如下：",
    JSON.stringify(payload)
  ].join("\n");

  const text = await createArkChatCompletion([
    {
      role: "user",
      content
    }
  ]);

  return parseJsonObject(text);
}

async function createVoiceDialogue({ audioPath, roleId, roleName, sceneId }) {
  if (!openai) {
    console.warn("[voice-dialogue] OPENAI_API_KEY is not set, return fallback dialogue.");
    return createDefaultDialogueResponse();
  }

  const transcript = await transcribeAudio(audioPath);
  const reply = await createRoleReply({ transcript, roleId, roleName, sceneId });
  const audioFileName = await createSpeechFile(reply.replyText);

  return {
    ok: true,
    transcript,
    replyText: reply.replyText,
    audioUrl: `${publicBaseUrl}/generated-audio/${audioFileName}`,
    speakingVideoNodeId: reply.speakingVideoNodeId,
    emotion: reply.emotion
  };
}

async function transcribeAudio(audioPath) {
  const transcription = await openai.audio.transcriptions.create({
    file: fs.createReadStream(audioPath),
    model: transcriptionModel,
    response_format: "text"
  });

  return typeof transcription === "string" ? transcription.trim() : toStringValue(transcription.text).trim();
}

async function createRoleReply({ transcript, roleId, roleName, sceneId }) {
  if (arkApiKey) {
    return createRoleReplyWithArk({ transcript, roleId, roleName, sceneId });
  }

  const response = await openai.responses.create({
    model,
    input: [
      {
        role: "system",
        content: [
          "你是互动影像游戏中的角色对话系统。",
          "你需要扮演当前角色，用简短、自然、有剧情感的中文回答玩家问题。",
          "不要超过 80 个中文字符。",
          "只返回符合 JSON Schema 的 JSON。",
          "speakingVideoNodeId 固定返回 role_speaking，用于 Unity 播放预制角色说话视频。"
        ].join("\n")
      },
      {
        role: "user",
        content: JSON.stringify({
          roleId,
          roleName,
          sceneId,
          playerQuestion: transcript
        })
      }
    ],
    text: {
      format: {
        type: "json_schema",
        name: "voice_dialogue_reply",
        strict: true,
        schema: {
          type: "object",
          additionalProperties: false,
          required: ["replyText", "speakingVideoNodeId", "emotion"],
          properties: {
            replyText: { type: "string" },
            speakingVideoNodeId: { type: "string", enum: ["role_speaking"] },
            emotion: { type: "string", enum: ["calm", "serious", "nervous", "encouraging"] }
          }
        }
      }
    }
  });

  if (!response.output_text) {
    throw new Error("OpenAI dialogue response.output_text is empty");
  }

  const parsed = JSON.parse(response.output_text);
  return {
    replyText: toStringValue(parsed.replyText) || "我听见了。现在先保持冷静，我们一步一步来。",
    speakingVideoNodeId: "role_speaking",
    emotion: toStringValue(parsed.emotion) || "calm"
  };
}

async function createRoleReplyWithArk({ transcript, roleId, roleName, sceneId }) {
  const content = [
    "你是互动影像游戏中的角色对话系统。",
    "你需要扮演当前角色，用简短、自然、有剧情感的中文回答玩家问题。",
    "不要超过 80 个中文字符。",
    "只能返回 JSON，不要返回 Markdown、解释、自然语言前后缀。",
    "JSON 字段必须是 replyText, speakingVideoNodeId, emotion。",
    "speakingVideoNodeId 固定返回 role_speaking。",
    JSON.stringify({
      roleId,
      roleName,
      sceneId,
      playerQuestion: transcript
    })
  ].join("\n");

  const text = await createArkChatCompletion([
    {
      role: "user",
      content
    }
  ]);

  const parsed = parseJsonObject(text);
  return {
    replyText: toStringValue(parsed.replyText) || "我听见了。现在先保持冷静，我们一步一步来。",
    speakingVideoNodeId: "role_speaking",
    emotion: toStringValue(parsed.emotion) || "calm"
  };
}

async function createSpeechFile(replyText) {
  const response = await openai.audio.speech.create({
    model: ttsModel,
    voice: ttsVoice,
    input: replyText,
    response_format: "mp3"
  });

  const audioBuffer = Buffer.from(await response.arrayBuffer());
  const fileName = `${crypto.randomUUID()}.mp3`;
  const outputPath = path.join(generatedAudioDir, fileName);
  await fs.promises.writeFile(outputPath, audioBuffer);
  return fileName;
}

function createDefaultDialogueResponse(errorMessage = "") {
  return {
    ok: false,
    transcript: "",
    replyText: "我听见了。现在先保持冷静，我们一步一步来。",
    audioUrl: "",
    speakingVideoNodeId: "role_speaking",
    emotion: "calm",
    error: errorMessage
  };
}

function normalizePayload(body) {
  const answers = Array.isArray(body && body.answers)
    ? body.answers.map((answer) => ({
        questionId: toStringValue(answer.questionId),
        questionText: toStringValue(answer.questionText),
        optionId: toStringValue(answer.optionId),
        optionText: toStringValue(answer.optionText)
      }))
    : [];

  return {
    playerId: toStringValue(body && body.playerId),
    answers
  };
}

async function createArkChatCompletion(messages) {
  const response = await fetch(`${arkBaseUrl}/chat/completions`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${arkApiKey}`
    },
    body: JSON.stringify({
      model: arkModel,
      reasoning_effort: arkReasoningEffort,
      messages
    })
  });

  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(`Ark chat completion failed: ${response.status} ${errorText}`);
  }

  const data = await response.json();
  const content = data &&
    data.choices &&
    data.choices[0] &&
    data.choices[0].message &&
    data.choices[0].message.content;

  if (Array.isArray(content)) {
    return content
      .map((part) => part.text || "")
      .join("")
      .trim();
  }

  if (typeof content === "string") {
    return content.trim();
  }

  throw new Error("Ark chat completion content is empty");
}

function parseJsonObject(text) {
  if (!text) {
    throw new Error("empty JSON text");
  }

  const trimmed = text.trim();
  try {
    return JSON.parse(trimmed);
  } catch (error) {
    const start = trimmed.indexOf("{");
    const end = trimmed.lastIndexOf("}");
    if (start >= 0 && end > start) {
      return JSON.parse(trimmed.slice(start, end + 1));
    }

    throw error;
  }
}

function normalizeRole(role) {
  if (!role || typeof role !== "object") {
    return DEFAULT_ROLE;
  }

  if (!ROLE_START_NODE[role.roleId]) {
    return DEFAULT_ROLE;
  }

  if (role.startNodeId !== ROLE_START_NODE[role.roleId]) {
    return DEFAULT_ROLE;
  }

  return {
    roleId: role.roleId,
    roleName: toStringValue(role.roleName) || DEFAULT_ROLE.roleName,
    roleDescription: toStringValue(role.roleDescription) || DEFAULT_ROLE.roleDescription,
    startNodeId: role.startNodeId,
    traits: Array.isArray(role.traits) ? role.traits.map(toStringValue).filter(Boolean) : []
  };
}

function toStringValue(value) {
  return typeof value === "string" ? value : "";
}

function normalizeEnvValue(value) {
  return typeof value === "string" ? value.trim() : "";
}

function isLikelyApiKey(value) {
  if (!value) {
    return false;
  }

  if (/[\u0080-\uFFFF]/.test(value)) {
    console.warn("[server] OPENAI_API_KEY contains non-ASCII characters and will be ignored.");
    return false;
  }

  if (value.includes("你的") || value.includes("your-") || value.includes("here")) {
    return false;
  }

  return true;
}

function getErrorMessage(error) {
  if (!error) {
    return "unknown error";
  }

  return error.stack || error.message || String(error);
}

app.listen(port, () => {
  console.log(`FMV demo server listening at http://localhost:${port}`);
  console.log(`Ark configured: ${Boolean(arkApiKey)}`);
  console.log(`Ark model: ${arkModel}`);
  console.log(`OpenAI configured: ${Boolean(openai)}`);
  console.log(`OpenAI model: ${model}`);
});

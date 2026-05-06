const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const MAX_RECORDS = 200;
const MAX_STRING_LENGTH = 1000;
const apiInteractionRecords = [];
let lastUpdatedAt = "";

function recordApiInteraction(record = {}) {
  const safeRecord = sanitizeRecord(record);
  apiInteractionRecords.push(safeRecord);

  while (apiInteractionRecords.length > MAX_RECORDS) {
    apiInteractionRecords.shift();
  }

  lastUpdatedAt = safeRecord.timestamp;
  return safeRecord;
}

async function saveApiInteractionMemory({ outputDir, metadata = {} } = {}) {
  const memoryId = crypto.randomUUID();
  const savedAt = new Date().toISOString();
  const fileName = buildMemoryFileName(savedAt);
  const safeMetadata = sanitizeRecord(metadata);
  const records = apiInteractionRecords.map((record) => ({ ...record }));
  const payload = {
    memoryId,
    savedAt,
    recordCount: records.length,
    metadata: safeMetadata,
    records
  };

  if (!outputDir) {
    return {
      ok: false,
      memoryId,
      fileName,
      recordCount: records.length,
      error: "MEMORY_OUTPUT_DIR_MISSING"
    };
  }

  await fs.promises.mkdir(outputDir, { recursive: true });
  const outputPath = path.join(outputDir, fileName);
  await fs.promises.writeFile(outputPath, JSON.stringify(payload, null, 2), "utf8");

  return {
    ok: true,
    saved: true,
    memoryId,
    fileName,
    filePath: outputPath,
    recordCount: records.length
  };
}

function getApiInteractionMemorySummary() {
  const endpoints = [...new Set(apiInteractionRecords.map((record) => record.endpoint).filter(Boolean))];
  return {
    ok: true,
    recordCount: apiInteractionRecords.length,
    endpoints,
    lastUpdatedAt
  };
}

function clearApiInteractionMemory() {
  apiInteractionRecords.length = 0;
  lastUpdatedAt = "";
}

function sanitizeRecord(record) {
  const timestamp = new Date().toISOString();
  const requestId = sanitizeString(record.requestId) || crypto.randomUUID();
  const safeRecord = {
    timestamp,
    requestId
  };

  const allowedKeys = [
    "endpoint",
    "roleId",
    "roleName",
    "sceneId",
    "source",
    "transcript",
    "replyText",
    "audioUrl",
    "speakingVideoNodeId",
    "emotion",
    "error",
    "status",
    "audioFileSize",
    "ttsAudioBytes",
    "answersCount",
    "startNodeId",
    "accessKeyConfigured",
    "recordCount",
    "fileName",
    "client"
  ];

  for (const key of allowedKeys) {
    if (!(key in record)) {
      continue;
    }

    const value = record[key];
    if (typeof value === "string") {
      safeRecord[key] = sanitizeString(value);
      continue;
    }

    if (typeof value === "number" && Number.isFinite(value)) {
      safeRecord[key] = value;
      continue;
    }

    if (typeof value === "boolean") {
      safeRecord[key] = value;
    }
  }

  return safeRecord;
}

function sanitizeString(value) {
  if (typeof value !== "string") {
    return "";
  }

  return value.trim().slice(0, MAX_STRING_LENGTH);
}

function buildMemoryFileName(savedAt) {
  const compact = savedAt
    .replace(/[-:]/g, "")
    .replace("T", "-")
    .replace(/\..*$/, "")
    .replace(/Z$/, "");

  return `memory-${compact}.json`;
}

module.exports = {
  recordApiInteraction,
  saveApiInteractionMemory,
  getApiInteractionMemorySummary,
  clearApiInteractionMemory
};

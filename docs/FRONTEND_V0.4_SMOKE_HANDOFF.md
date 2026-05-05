# 前端 v0.4 语音链路 Smoke 合并交接报告

## 1. 分支信息

- 前端分支：frontend/doubao-smoke-v0.4
- 来源分支：integration/v0.2.0
- 后端相关分支：backend/render-doubao-v0.3
- 最新前端 commit：本文件随本次提交一起生成，最终 commit 以 `git log --oneline -1` 为准

## 2. 本轮目标

前端负责 Unity 端真实语音链路 Smoke：

录音 → 上传 `/api/voice-dialogue` → 显示 `transcript` / `replyText` → `audioUrl` 为空不报错 → `audioUrl` 有值尝试播放。

前端不实现豆包 WebSocket / ASR / LLM / TTS，这些由后端负责。

## 3. 修改文件列表

主要前端改动集中在：

- `Assets/Resources/story.json`
- `Assets/Scripts/FMVDemoController.cs`
- `Assets/Scripts/OpeningQuestionController.cs`
- `Assets/Scripts/PauseMenuController.cs`
- `Assets/Scripts/VoiceDialogueController.cs`
- `Assets/StreamingAssets/Videos/`
- `Assets/UI/Fonts/SIMFANG SDF.asset`
- `docs/FRONTEND_V0.4_SMOKE_HANDOFF.md`

## 4. 完成内容

- 录音采样率使用 `16000 Hz`。
- 录音结果通过 `WavUtility.FromAudioClip(...)` 输出 WAV。
- WAV 写入格式为 16-bit PCM。
- `/api/voice-dialogue` 上传字段保持 `audio` / `roleId` / `roleName` / `sceneId`。
- 兼容 `source=doubao` / `source=mock` / `source` 缺失的返回。
- `transcript` 显示到 `TranscriptText`。
- `replyText` 显示到 `ReplyText`。
- `audioUrl` 为空时安全跳过音频播放。
- `audioUrl` 有值时根据后缀尝试播放 `.mp3` / `.wav`。
- 语音请求失败、JSON 解析失败、`audioUrl` 缺失等情况不会阻塞 UI。
- `RecordButton` 停止录音后隐藏，处理结束后恢复。
- dialogue 初始视频、成功说话视频和兜底视频播放链路已在前端接入。
- Esc 菜单 dialogue 入口仍通过 story node 跳转，不直接绕过 `FMVDemoController`。

## 5. 录音格式说明

- Microphone.Start 采样率：`16000`
- WAV：使用 `WavUtility.FromAudioClip(...)` 输出
- 16-bit PCM：已确认，`WavUtility` 将 float samples 转为 16-bit PCM little-endian
- mono 是否确认：不能完全确认

Unity 当前无法完全确认 mono，录音声道数使用 `AudioClip.channels`。若设备返回双声道，可能需要后端兼容或转码。

## 6. 接口字段

`voice-dialogue` 上传字段保持：

- `audio`
- `roleId`
- `roleName`
- `sceneId`

本轮没有修改后端接口字段。

## 7. 返回显示兼容

- transcript：非空时显示后端文本；为空时显示“未识别到语音文本”
- replyText：非空时显示后端文本；为空时显示“未收到角色回复”
- source=doubao：状态提示“真实语音回复”
- source=mock：状态提示“Demo 模式回复”
- source 缺失：正常显示“已收到回复”
- error 字段：记录 warning，不阻塞文本显示
- audioUrl 为空：跳过音频播放，不报错
- audioUrl 有值：按 `.wav` / `.mp3` 后缀尝试播放，失败时保留文本显示

## 8. Smoke 测试结果

- Smoke 1 跳过 Demo：未完成本轮实机验证
- Smoke 2 错误 Key fallback：未完成实机验证
- Smoke 3 真实 Key 联调：未完成实机验证

## 9. 真实语音测试内容

未完成真实 Key 联调。

- 玩家说的话：未测试
- TranscriptText 显示：未测试
- ReplyText 显示：未测试
- audioUrl：未测试

## 10. Console 报错

未完成本轮 Unity Editor 实机复测，因此暂无新的 Console 结论。合并后需要总控在 Unity Editor 中重新确认是否有阻塞性红错。

## 11. 安全确认

- 未打印 Access Key
- 未保存 Access Key
- 未提交 server/
- 未提交 server/.env
- 未提交真实 Key
- 未提交 Build_Windows/
- 未提交 Build_WebGL/
- 未提交 release/
- 未提交 zip

## 12. 仍未完成

- Smoke 1 跳过 Demo：未完成本轮实机验证
- Smoke 2 错误 Key fallback：未完成实机验证
- Smoke 3 真实 Key 联调：未完成实机验证
- mono 仍需后端确认或兼容
- Windows 包未打
- Release 未创建

## 13. 合并注意事项

- 建议合并到 `integration/v0.2.0`
- 不建议直接合 `master`
- 合并后必须由总控做 Smoke 1 / Smoke 2 / Smoke 3
- 后端需已支持 `/api/runtime-config/doubao` 和 `/api/voice-dialogue`
- 合并后重点测试：录音上传、`transcript` 显示、`replyText` 显示、`audioUrl` 为空和有值两种分支、兜底视频播放、RecordButton 恢复

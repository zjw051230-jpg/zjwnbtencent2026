# 前端 v0.6 保存记忆按钮完成报告

## 1. 前端目录

D:\HXY\Data\Unity\My project

## 2. 分支名

frontend/save-memory-button-v0.6

## 3. 最新 commit hash

提交后填写。

## 4. GitHub 分支链接

https://github.com/zjw051230-jpg/zjwnbtencent2026/tree/frontend/save-memory-button-v0.6

## 5. 修改文件列表

本轮计划提交的前端文件以最终 commit 为准，当前包括：

- Assets/Resources/story.json
- Assets/Scripts/FMVDemoController.cs
- Assets/Scripts/OpeningQuestionController.cs
- Assets/Scripts/PauseMenuController.cs
- Assets/Scripts/VoiceDialogueController.cs
- Assets/StreamingAssets/Videos/main_scene_01.mp4
- Assets/StreamingAssets/Videos/main_scene_01.mp4.meta
- Assets/StreamingAssets/Videos/mozart_main3.mp4
- Assets/StreamingAssets/Videos/mozart_main3.mp4.meta
- Assets/StreamingAssets/Videos/mozart_main4.mp4
- Assets/StreamingAssets/Videos/mozart_main4.mp4.meta
- Assets/StreamingAssets/Videos/mozart_main5.mp4
- Assets/StreamingAssets/Videos/mozart_main5.mp4.meta
- docs/FRONTEND_SAVE_MEMORY_BUTTON_V0.6_HANDOFF.md

## 6. 完成内容

- Esc 菜单新增保存记忆按钮：已完成。
- 调用 /api/memory/save：已完成，使用 POST http://localhost:3000/api/memory/save。
- 请求体字段：仅 client / sceneId / roleId / roleName。
- 保存状态提示：保存中、保存成功、保存失败、后端未连接均有提示。
- 保存中防重复点击：已增加 isSavingMemory 和 coroutine 双重保护，保存中按钮禁用。
- 后端未启动处理：UnityWebRequest 失败时显示保存失败提示，不崩溃。
- 不影响进入语音对话：原 story menu node 跳转逻辑保留。
- 不影响 AI 语音播放：dialogue_scene 录音、transcript/replyText、audioUrl 播放逻辑未为保存记忆而改动。
- 不保存 / 不打印 Access Key：保存记忆请求不包含 Access Key，也不打印 Access Key。

同时包含已确认的前端修复：

- Esc 菜单角色线跳转会停止旧问答流程，避免回到四题问答。
- Mozart dialogue fallback 播放后与 success 一样进入 dialogue_Mozart.defaultNext。
- mozart_main4 支持点击画面进入下一节点，不使用普通按钮。

## 7. /api/memory/save 请求说明

请求地址：

POST http://localhost:3000/api/memory/save

请求体：

```json
{
  "client": "unity",
  "sceneId": "dialogue_scene",
  "roleId": "...",
  "roleName": "..."
}
```

确认：

- 不包含 Access Key
- 不包含 Token
- 不包含 Secret

## 8. Smoke 测试结果

- Smoke A 后端未启动：未完成实机验证。之前检查时本地后端仍在运行，因此未形成有效后端未启动测试。
- Smoke B 后端启动无记录：未完成实机验证。已确认 /health 正常，但未能在 Unity Editor 中完成实际点击验证。
- Smoke C 真实语音互动后保存：未完成实机验证。
- Smoke D 保存后继续对话：未完成实机验证。

说明：本报告不虚构通过结果。合并后需要总控在 Unity Editor 中完成 A-D 实机 Smoke。

## 9. 安全确认

- 未保存 Access Key：是。
- 未打印 Access Key：是。
- 未提交 server/：本分支提交前需再次确认。
- 未提交 server/.env：本分支提交前需再次确认。
- 未提交真实 Key：本分支提交前需再次确认。
- 未提交 build / zip：本分支提交前需再次确认。
- 未直接 push integration：是。

## 10. 仍未完成的问题

- Smoke A-D 未完成实机验证。
- 需要总控合并后测试 Esc 菜单保存记忆、进入语音对话、AI 语音播放。
- 后端 POST /api/memory/save 仍需与当前前端请求体做集成联调。
- 当前工作区包含 Mozart main3/main4/main5 视频资源增删及 main_scene_01 删除，需要合并人确认这些资源变化属于本轮前端流程更新。

## 11. 合并注意事项

- 请合并到 integration/v0.2.0。
- 不建议直接合 master。
- 合并后重点测 Esc 菜单、保存记忆、进入语音对话、AI 语音播放。
- 后端需提供 POST /api/memory/save。

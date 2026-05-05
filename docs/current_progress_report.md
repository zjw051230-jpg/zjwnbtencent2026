# FMV Demo 当前进度汇报

更新时间：2026-05-05

## 1. 项目目标

当前项目是一个 Unity WebGL/Editor 可运行的互动影像 Demo。

目标流程：

```text
开场四题问答
↓
后端分配角色
↓
Unity 根据角色入口播放 intro 视频
↓
进入主剧情视频 main_scene_01
↓
剧情选项分支
↓
进入普通结局或语音对话事件
↓
语音对话中玩家说话，后端 AI 回复
↓
Unity 显示 AI 回复文本，播放 AI 语音，同时播放角色说话视频
```

## 2. 当前目录结构

关键目录：

```text
D:\HXY\Data\Unity\My project
  Assets/
    Resources/
      story.json
    Scripts/
      FMVDemoController.cs
      OpeningQuestionController.cs
      VoiceDialogueController.cs
    StreamingAssets/
      Videos/
        intro_Mozart.mp4
        intro_investigator.mp4
  server/
    package.json
    package-lock.json
    server.js
    .env.example
    .env
  docs/
    backend_requirements.md
    current_progress_report.md
```

## 3. Unity 已完成内容

### 3.1 开场问答系统

脚本：

```text
Assets/Scripts/OpeningQuestionController.cs
```

已实现：

```text
显示 4 个问题
动态生成选项按钮
记录玩家选择
四题完成后 POST 到后端 /api/assign-role
接收角色 JSON
保存角色信息到 PlayerPrefs
调用 FMVDemoController.PlayNodeFromOutside(startNodeId)
第四题结束后隐藏 titleText / questionText / progressText
答题阶段隐藏 RawImageVideo，避免显示上次视频残帧
```

本地保存的 PlayerPrefs key：

```text
PLAYER_ROLE_ID
PLAYER_ROLE_NAME
PLAYER_ROLE_DESC
PLAYER_START_NODE
```

### 3.2 FMV 剧情播放系统

脚本：

```text
Assets/Scripts/FMVDemoController.cs
```

已实现：

```text
读取 Assets/Resources/story.json
根据 node id 播放 StreamingAssets/Videos 下的视频
支持 WebGL 视频路径
支持 choiceTime 到点弹出剧情选项
支持 choices.next 分支跳转
支持 defaultNext 视频播完自动跳转
支持外部入口 PlayNodeFromOutside(string nodeId)
支持 eventType = voiceDialogue 的特殊事件节点
支持进入语音对话节点时打开 VoiceDialogueController
播放前清理 RenderTexture 残帧
视频显示层 videoDisplay 播放时显示，非播放阶段隐藏
```

### 3.3 剧情配置

文件：

```text
Assets/Resources/story.json
```

当前核心节点：

```text
intro_Mozart
intro_protector
intro_survivor
main_scene_01
investigate
escape
dialogue_scene
end_good
end_bad
```

当前 dialogue 测试路径：

```text
main_scene_01
↓
choiceTime = 2 秒后出现选项
↓
点击 To dialogue test
↓
进入 dialogue_scene
↓
eventType = voiceDialogue
↓
打开 DialoguePanel
```

`dialogue_scene` 配置：

```json
{
  "id": "dialogue_scene",
  "video": "",
  "eventType": "voiceDialogue",
  "choiceTime": -1,
  "defaultNext": "end_good",
  "choices": []
}
```

### 3.4 语音对话系统

脚本：

```text
Assets/Scripts/VoiceDialogueController.cs
```

已实现：

```text
DialoguePanel 默认隐藏
进入 voiceDialogue 事件时打开 DialoguePanel
点击 RecordButton 开始录音
再次点击停止录音并上传
把录音转成 wav
上传到后端 /api/voice-dialogue
接收 transcript / replyText / audioUrl / speakingVideoNodeId
显示 transcript 到 TranscriptText
显示 replyText 到 ReplyText
播放 audioUrl 对应的 mp3
同时播放本地角色说话视频 role_speaking.mp4
```

当前限制：

```text
Unity Editor / Windows 桌面可用 Microphone 测试
WebGL 录音需要额外浏览器麦克风 JS 插件
```

## 4. 后端已完成内容

后端目录：

```text
server/
```

核心文件：

```text
server/server.js
server/package.json
server/.env.example
server/.env
```

### 4.1 启动方式

```powershell
cd "D:\HXY\Data\Unity\My project\server"
npm start
```

健康检查：

```text
GET http://localhost:3000/health
```

正常返回示例：

```json
{
  "ok": true,
  "service": "fmv-demo-server",
  "arkConfigured": true,
  "openaiConfigured": false
}
```

### 4.2 角色分配接口

接口：

```text
POST /api/assign-role
```

当前状态：

```text
已临时固定为 Mozart
无论玩家四题选择什么，都返回 Mozart
```

固定返回：

```json
{
  "roleId": "Mozart",
  "roleName": "Mozart",
  "roleDescription": "冷静观察线索，适合从调查者入口进入剧情。",
  "startNodeId": "intro_Mozart",
  "traits": ["observe", "logic", "risk_control"]
}
```

### 4.3 语音对话接口

接口：

```text
POST /api/voice-dialogue
```

请求格式：

```text
multipart/form-data
```

字段：

```text
audio    玩家录音 wav 文件
roleId   当前角色 ID
roleName 当前角色名
sceneId  当前对话场景 ID
```

目标返回：

```json
{
  "ok": true,
  "transcript": "玩家说的话",
  "replyText": "AI 回复文本",
  "audioUrl": "http://localhost:3000/generated-audio/xxx.mp3",
  "speakingVideoNodeId": "role_speaking",
  "emotion": "calm"
}
```

当前实际状态：

```text
Unity 已经可以录到 wav
后端接口可以接收上传
Ark 聊天接口已配置到后端，用于生成文本回复
OpenAI Key 当前不可用/未配置，因此语音转文字和 TTS 还不能正常工作
```

当前兜底返回：

```json
{
  "ok": false,
  "transcript": "",
  "replyText": "我听见了。现在先保持冷静，我们一步一步来。",
  "audioUrl": "",
  "speakingVideoNodeId": "role_speaking",
  "emotion": "calm",
  "error": "具体错误信息"
}
```

## 5. AI 配置状态

### 5.1 Ark / 火山方舟

已接入：

```text
ARK_API_KEY
ARK_BASE_URL=https://ark.cn-beijing.volces.com/api/v3
ARK_MODEL=doubao-seed-2-0-pro-260215
ARK_REASONING_EFFORT=medium
```

用途：

```text
角色分配文本逻辑，当前已被固定 Mozart 暂时绕过
语音对话中的 AI 文本回复
```

注意：

```text
当前接入的是 Ark Chat Completions
它不是语音转文字 ASR，也不是文字转语音 TTS
```

### 5.2 OpenAI

当前状态：

```text
没有可用 OpenAI Key
openaiConfigured = false
```

影响：

```text
无法把玩家语音转成 transcript
无法生成 AI 回复 mp3
所以 VoiceDialogueController 暂时只能收到兜底 replyText
```

如果后续要不用 OpenAI，需要提供火山方舟的：

```text
ASR 语音转文字接口 curl 示例
TTS 文字转语音接口 curl 示例
```

## 6. Unity 需要绑定的对象

### 6.1 OpeningQuestionController

需要绑定：

```text
Question Panel
Title Text
Question Text
Progress Text
Loading Text
Option Root
Option Button Prefab
FMV Controller
Video Display
```

`Video Display` 建议绑定：

```text
RawImageVideo
```

### 6.2 FMVDemoController

需要绑定：

```text
Video Player
Video Display
Choice Panel
Choice Button Root
Choice Button Prefab
Voice Dialogue Controller
```

说明：

```text
Voice Dialogue Controller 可手动拖入
脚本也会尝试自动 FindObjectOfType
```

### 6.3 VoiceDialogueController

需要绑定：

```text
Dialogue Panel
Record Button
Status Text
Transcript Text
Reply Text
AI Audio Source
Speaking Video Player
Speaking Video Display
```

### 6.4 视频显示

主视频：

```text
VideoPlayer.TargetTexture = RT_Video
RawImageVideo.RawImage.Texture = RT_Video
RawImageVideo.RaycastTarget = false
```

角色说话视频：

```text
SpeakingVideoPlayer.TargetTexture = RT_RoleSpeaking
SpeakingVideoDisplay.RawImage.Texture = RT_RoleSpeaking
SpeakingVideoDisplay.RaycastTarget = false
```

## 7. 当前已知问题

### 7.1 中文显示

问题：

```text
TextMeshPro 默认 LiberationSans SDF 不支持中文
中文会显示成方块
```

解决：

```text
导入中文字体，例如 simhei.ttf / Microsoft YaHei / Noto Sans CJK
Create > TextMeshPro > Font Asset
Atlas Population Mode 设置为 Dynamic
把 QuestionText / ReplyText / TranscriptText / Button Text 的 Font Asset 换成中文 TMP Font Asset
```

### 7.2 语音转文字暂不可用

原因：

```text
没有可用 OpenAI Key
当前 Ark 接口只接了 Chat Completions，不是 ASR/TTS
```

结果：

```text
transcript 为空
replyText 总是兜底文本
audioUrl 为空
```

### 7.3 WebGL 录音未完成

当前：

```text
VoiceDialogueController 使用 Unity Microphone
适合 Editor / Windows 桌面测试
```

后续：

```text
WebGL 需要浏览器麦克风 JS 插件
```

### 7.4 视频资源不完整

当前 Videos 目录至少有：

```text
intro_Mozart.mp4
intro_investigator.mp4
```

story.json 还引用：

```text
main_scene_01.mp4
investigate.mp4
escape.mp4
end_good.mp4
end_bad.mp4
role_speaking.mp4
```

这些文件需要补齐，否则对应节点会播放失败。

## 8. 当前测试方式

### 8.1 启动后端

```powershell
Get-Process node | Stop-Process -Force
cd "D:\HXY\Data\Unity\My project\server"
npm start
```

检查：

```text
http://localhost:3000/health
```

### 8.2 测试开场流程

```text
Unity Play
↓
回答四题
↓
后端固定返回 Mozart
↓
播放 intro_Mozart.mp4
↓
进入 main_scene_01
```

### 8.3 测试 dialogue 入口

```text
main_scene_01 播放到 choiceTime = 2 秒
↓
点击 To dialogue test
↓
进入 dialogue_scene
↓
DialoguePanel 打开
```

Console 应看到：

```text
FMVDemoController: opened voice dialogue node dialogue_scene
VoiceDialogueController: dialogue panel opened.
```

### 8.4 测试录音

点击录音按钮：

```text
第一次点击：开始录音
第二次点击：停止录音并发送
```

Console 应看到：

```text
VoiceDialogueController: microphones = ...
VoiceDialogueController: recording started with ...
VoiceDialogueController: microphone sample position = ...
VoiceDialogueController: wav bytes = ...
VoiceDialogueController: response ...
```

如果 response 是兜底：

```text
看 error 字段
```

## 9. 下一步建议

优先级从高到低：

```text
1. 补齐 main_scene_01.mp4 / role_speaking.mp4 等视频资源
2. 确认 DialoguePanel、TranscriptText、ReplyText、RecordButton 绑定正确
3. 接入可用 ASR/TTS
   - 要么填 OpenAI Key
   - 要么提供火山方舟 ASR/TTS curl 示例，我改成全 Ark
4. 导入中文 TMP 字体，解决中文方块
5. 做 WebGL 录音插件
6. 清理 story.json 中的测试节点和正式剧情节点
```

## 10. 当前结论

当前 Demo 的主线框架已经搭好：

```text
开场问答
角色入口
FMV 播放
剧情选项
dialogue 事件节点
语音录音上传
后端 AI 回复接口
```

目前最大的阻塞是：

```text
没有可用语音转文字 / 文字转语音服务
```

所以语音对话暂时只能验证：

```text
录音能生成 wav
上传接口能收到
DialoguePanel 能打开
兜底回复能返回
```

要完整实现“玩家说话 -> 显示玩家说的话 -> AI 回复 -> 播放 AI 语音”，需要接入可用 ASR 和 TTS。

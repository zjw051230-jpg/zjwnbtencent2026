# Unity 客户端发布测试说明 v0.3

## 1. 分支信息

前端分支：

frontend/release-client-v0.3

后端分支：

backend/render-doubao-v0.3

集成分支：

integration/v0.2.0

## 2. 后端地址配置

Unity 客户端现在通过 apiBaseUrl 配置后端地址。

本地后端：

http://localhost:3000

Render 后端：

https://你的-render-service.onrender.com

说明：
不要把豆包 App ID / Access Key 写入 Unity。
真实 Key 只应放在 Render 环境变量中。

## 3. Inspector 配置位置

需要在 Unity Inspector 中确认：

OpeningQuestionController:

apiBaseUrl = http://localhost:3000

或

apiBaseUrl = https://你的-render-service.onrender.com

VoiceDialogueController:

apiBaseUrl = http://localhost:3000

或

apiBaseUrl = https://你的-render-service.onrender.com

## 4. 本地测试流程

1. 启动本地后端：

```powershell
cd server
npm start
```

2. 浏览器检查：

http://localhost:3000/health

3. Unity Play：

```text
黑屏开场音频
↓
四题问答
↓
assign-role 返回 Mozart
↓
播放 intro_Mozart
↓
进入 main_scene_01
↓
进入 dialogue_scene
↓
DialoguePanel 打开
↓
点击 RecordButton
↓
录音上传
↓
显示 transcript / replyText
```

## 5. Render 测试流程

1. 确认 Render 后端已启动。
2. 打开 Render /health 地址。
3. 将 Unity Inspector 中两个 apiBaseUrl 都改成 Render URL。
4. Unity Play。
5. 重复完整流程。
6. Render 冷启动可能需要等待几十秒。

## 6. DEMO_VOICE_MOCK 说明

如果后端开启 DEMO_VOICE_MOCK，voice-dialogue 会返回模拟数据：

transcript:

我刚才听到了一些奇怪的声音，你觉得我们现在应该怎么办？

replyText:

先别慌。我们先确认声音的来源，再决定要不要继续往前走。

audioUrl:

空字符串

前端要求：
audioUrl 为空时跳过播放，不报错。
TranscriptText 和 ReplyText 正常显示。

## 7. Windows 包测试

Windows 包目录示例：

```text
Build_Windows/
  FMV-Demo.exe
  FMV-Demo_Data/
  UnityPlayer.dll
```

压缩包建议命名：

FMV-Demo-v0.3.0-Windows.zip

注意：
Build_Windows/ 和 zip 不要提交 Git。
它们应作为 GitHub Release asset 上传。

## 8. 验收清单

- Unity Editor + localhost 后端通过
- Unity Editor + Render 后端通过
- Windows 包 + Render 后端通过
- assign-role 能返回 Mozart
- voice-dialogue 能返回 transcript
- voice-dialogue 能返回 replyText
- audioUrl 为空时不报错
- audioUrl 有值时能尝试播放
- 中文正常显示
- RecordButton 能开始/停止录音
- ReplyText 能显示
- TranscriptText 能显示

## 9. 常见问题

### Cannot connect to destination host

检查：

- 后端是否启动
- apiBaseUrl 是否正确
- Render 是否冷启动中
- 网络是否能访问 Render

### audioUrl 为空

这是 DEMO_VOICE_MOCK 下的正常情况。
前端应跳过音频播放。

### 中文显示方块

检查 TMP 字体是否使用 SIMFANG SDF。

### Windows 包无法连接 localhost

如果测试 Windows 包连接 Render，需要把 apiBaseUrl 配成 Render URL 后再打包。

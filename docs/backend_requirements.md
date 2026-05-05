# FMV Demo 后端程序要求与验收标准

## 1. 后端目标

后端用于连接 Unity 问答系统和 OpenAI API。

完整流程：

```text
Unity 收集玩家开场四题答案
↓
POST 到后端 /api/assign-role
↓
后端调用 OpenAI 分析玩家角色
↓
后端返回固定结构角色 JSON
↓
Unity 根据 startNodeId 播放角色入口视频
```

要求：

- OpenAI API Key 只能放在后端环境变量中。
- Unity 不直接调用 OpenAI。
- Unity 不保存、不暴露 OpenAI API Key。
- 后端必须保证返回给 Unity 的 JSON 结构固定。
- OpenAI 出错时，后端必须返回默认角色，不能阻断游戏流程。

## 2. 目录结构

后端目录位于 Unity 项目根目录：

```text
server/
  package.json
  package-lock.json
  server.js
  .env.example
  .env
```

说明：

- `package.json`：Node.js 项目配置和启动命令。
- `server.js`：Express 后端入口。
- `.env.example`：环境变量示例，可以提交。
- `.env`：真实环境变量，不提交，不打包。

## 3. 运行环境

后端使用 Node.js。

依赖：

```text
express  - Web API 服务
cors     - 允许 Unity Editor / WebGL 本地访问
dotenv   - 读取 .env 环境变量
openai   - 调用 OpenAI API
```

安装和启动：

```powershell
cd server
npm install
npm start
```

默认服务地址：

```text
http://localhost:3000
```

## 4. 环境变量

`.env.example` 至少包含：

```env
PORT=3000
OPENAI_API_KEY=sk-your-api-key-here
OPENAI_MODEL=gpt-4.1-mini
```

要求：

- `OPENAI_API_KEY` 只能出现在后端 `.env` 中。
- 不允许把 API Key 写进 Unity 脚本。
- 不允许把 API Key 写进 `story.json`。
- 不允许把 API Key 打包进 WebGL。
- `.env` 不提交到版本库。

## 5. 接口一：健康检查

```text
GET /health
```

用途：

确认后端是否启动。

成功响应：

```json
{
  "ok": true
}
```

验收标准：

浏览器访问：

```text
http://localhost:3000/health
```

应返回：

```json
{
  "ok": true
}
```

## 6. 接口二：角色分配

```text
POST /api/assign-role
```

请求头：

```http
Content-Type: application/json
```

Unity 请求体：

```json
{
  "playerId": "device-or-player-id",
  "answers": [
    {
      "questionId": "q1",
      "questionText": "危机来临时，你第一反应是什么？",
      "optionId": "q1_a",
      "optionText": "保护别人"
    },
    {
      "questionId": "q2",
      "questionText": "面对陌生人的求助，你会怎么做？",
      "optionId": "q2_b",
      "optionText": "先判断真假"
    },
    {
      "questionId": "q3",
      "questionText": "你更相信什么？",
      "optionId": "q3_b",
      "optionText": "证据"
    },
    {
      "questionId": "q4",
      "questionText": "你最不能接受什么？",
      "optionId": "q4_a",
      "optionText": "背叛"
    }
  ]
}
```

后端响应必须是固定结构 JSON：

```json
{
  "roleId": "investigator",
  "roleName": "调查者",
  "roleDescription": "冷静观察线索，适合从调查者入口进入剧情。",
  "startNodeId": "intro_investigator",
  "traits": ["observe", "logic", "risk_control"]
}
```

## 7. 角色字段要求

字段说明：

```text
roleId           必填，字符串
roleName         必填，字符串
roleDescription  必填，字符串
startNodeId      必填，字符串
traits           必填，字符串数组，可以为空数组
```

允许的角色映射：

```text
investigator -> intro_investigator
protector    -> intro_protector
survivor     -> intro_survivor
```

要求：

- `roleId` 只能是 `investigator`、`protector`、`survivor`。
- `startNodeId` 必须和 `roleId` 对应。
- `startNodeId` 必须存在于 `Assets/Resources/story.json`。
- 不允许返回 Unity 无法播放的节点 ID。

## 8. AI 接入要求

后端调用 OpenAI 时，必须约束 AI 只返回固定 JSON 结构。

要求：

- 使用后端环境变量 `process.env.OPENAI_API_KEY`。
- Unity 不直接请求 OpenAI。
- Unity 不知道 OpenAI API Key。
- 后端使用 JSON Schema 或等价方式约束 AI 输出。
- 不允许把 AI 的自然语言解释直接转发给 Unity。
- AI 返回异常结构时，后端必须返回默认角色。

默认角色：

```json
{
  "roleId": "investigator",
  "roleName": "调查者",
  "roleDescription": "冷静观察线索，适合从调查者入口进入剧情。",
  "startNodeId": "intro_investigator",
  "traits": ["observe", "logic", "risk_control"]
}
```

## 9. 错误处理要求

以下情况都不能让接口直接失败，必须返回默认角色：

```text
OPENAI_API_KEY 未配置
OpenAI 请求失败
OpenAI 超时
OpenAI 返回内容不是 JSON
OpenAI 返回 JSON 缺字段
OpenAI 返回了不存在的 roleId
OpenAI 返回了错误的 startNodeId
请求体为空
answers 为空
```

Unity 最终必须收到一个可播放的角色入口：

```json
{
  "roleId": "investigator",
  "roleName": "调查者",
  "roleDescription": "冷静观察线索，适合从调查者入口进入剧情。",
  "startNodeId": "intro_investigator",
  "traits": ["observe", "logic", "risk_control"]
}
```

## 10. 日志要求

后端至少打印：

```text
服务启动端口
收到 /api/assign-role 请求
请求中的 playerId
answers 数量
AI 调用是否成功
最终返回的 roleId
最终返回的 startNodeId
AI 出错时的错误原因
```

日志中禁止打印：

```text
OPENAI_API_KEY
完整密钥
敏感环境变量
```

## 11. CORS 要求

第一版允许 Unity Editor、WebGL 本地页面访问。

第一版可使用：

```js
app.use(cors());
```

上线版本再限制来源。

## 12. 验收标准

### 12.1 后端能启动

执行：

```powershell
cd server
npm install
npm start
```

预期：

```text
FMV demo server listening at http://localhost:3000
```

### 12.2 健康检查通过

访问：

```text
http://localhost:3000/health
```

预期：

```json
{
  "ok": true
}
```

### 12.3 角色接口可用

执行：

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:3000/api/assign-role" `
  -ContentType "application/json" `
  -Body '{"playerId":"test","answers":[]}'
```

预期：

返回合法角色 JSON，至少包含：

```json
{
  "roleId": "investigator",
  "roleName": "调查者",
  "startNodeId": "intro_investigator"
}
```

### 12.4 未配置 OpenAI API Key 时仍然可用

操作：

```text
删除 .env 中的 OPENAI_API_KEY
重启后端
再次请求 /api/assign-role
```

预期：

```text
接口不报错
返回默认角色 investigator
Unity 可以继续进入 intro_investigator
```

### 12.5 OpenAI 出错时返回默认角色

模拟方式：

```text
填入错误 OPENAI_API_KEY
断网
设置错误模型名
```

预期：

```text
后端 Console 打印错误原因
接口仍返回默认角色
Unity 不崩溃
Unity 不停在问答界面
Unity 继续进入 intro_investigator
```

### 12.6 返回结构固定

无论 AI 如何响应，Unity 最终收到的 JSON 都必须符合：

```json
{
  "roleId": "string",
  "roleName": "string",
  "roleDescription": "string",
  "startNodeId": "string",
  "traits": []
}
```

不得返回：

```text
Markdown
解释文本
多余自然语言
非 JSON 字符串
缺 startNodeId 的对象
不存在于 story.json 的节点 ID
```

### 12.7 与 Unity 联调通过

完整流程：

```text
Unity 显示 4 道题
↓
玩家点击 4 次选项
↓
Unity POST answers JSON 到 /api/assign-role
↓
后端返回角色 JSON
↓
Unity 保存 roleId / roleName / startNodeId
↓
Unity 调用 FMVDemoController.PlayNodeFromOutside(startNodeId)
↓
播放 intro_investigator / intro_protector / intro_survivor
↓
视频结束进入 main_scene_01
↓
玩家选择剧情分支
↓
进入 end_good 或 end_bad
```

### 12.8 默认角色必须匹配 story.json

`Assets/Resources/story.json` 必须包含：

```text
intro_investigator
intro_protector
intro_survivor
main_scene_01
investigate
escape
end_good
end_bad
```

默认角色入口必须是：

```text
intro_investigator
```

如果 `intro_investigator` 不存在，验收不通过。

## 13. 第一版完成定义

第一版后端完成需要满足：

```text
server 能启动
/health 正常
/api/assign-role 正常
Unity 能发送 answers JSON
后端能返回角色 JSON
OpenAI API Key 只在后端
OpenAI 失败时返回默认角色
返回 JSON 结构固定
startNodeId 能被 Unity 剧情系统播放
```

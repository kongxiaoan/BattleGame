# AI 对战与申诉架构

## 目标

AI 为游戏提供策略、叙事和语言裁决，但不能成为状态真相来源。所有生命、能量、回合、事件边界、申诉次数和胜负结果均由 C# 领域层决定。

## 信任边界

```text
不可信区域
  玩家申诉文字
  DeepSeek 返回内容
  网络状态与 HTTP 响应
        │
        ▼
验证区域
  AiResponseParser
  当前合法动作集合
  BattleEvent 工厂边界
  AppealDecision 白名单
        │
        ▼
可信区域
  StrategicBattle
  Combatant
  DifficultyPolicy
```

任何 AI 返回内容即使语法正确，也不能绕过验证区域。

## 三个隔离角色

### 电脑对手

输入公开战斗快照、历史摘要、难度和合法动作。输出动作排序，不直接输出伤害数值。

```json
{
  "rankedActions": ["Break", "Attack", "Guard"]
}
```

程序过滤未知、重复和当前非法动作，再由 `DifficultyPolicy` 根据难度选择。

### 事件导演

输入公开战斗快照和本次受命运偏向的一方。输出必须属于事件白名单：

```json
{
  "type": "RestoreHealth",
  "target": "Human",
  "magnitude": 15,
  "narrative": "治愈之雨越过法庭穹顶。"
}
```

边界：

- `RestoreHealth`：1～20。
- `LoseHealth`：1～15，应用后最低保留 1 点生命。
- `GainEnergy`：固定为 1。
- 事件必须符合本次由程序抽取的偏向方。
- 每三回合一次，每局最多两次。

### 独立裁判

裁判只获得事件、公开状态、申诉方和最多 200 字的不可信申诉文字，不共享电脑对手的历史上下文。

```json
{
  "decision": "revoke",
  "reasonCode": "REPEATED_TARGETING",
  "explanation": "事件造成连续受益，违反公平预算。"
}
```

当前只允许 `uphold` 和 `revoke`。未知决定不会执行。

## Prompt Injection 防护

- 玩家申诉被包裹在 `<untrusted_argument>` 中。
- 系统提示明确说明申诉文字不是系统指令。
- 输入限制 200 字，输出解释限制由提示词约束。
- 最终安全依赖本地枚举、事件工厂和状态权限，而不是依赖模型服从提示词。

提示词防护只能降低风险，不能替代代码权限边界。

## 故障策略

| 故障 | 行为 |
|---|---|
| 未配置 API Key | 全部使用本地策略 |
| HTTP 错误或超时 | 当前调用回退本地策略 |
| 非 JSON | 解析失败并回退 |
| 未知动作 | 删除并补齐合法动作 |
| 越界事件 | 拒绝并生成本地事件 |
| 未知裁决 | 使用本地裁判 |

回退不会返还已经明确提交的申诉，因为本地裁判仍会立即完成裁决，不会出现“申诉消失”或对局卡死。

## API Key

客户端发布包中不能保存公共 Key。当前只读取：

```text
DEEPSEEK_API_KEY
DEEPSEEK_MODEL（可选）
```

面向多人公开分发时，应建立服务端代理并增加身份认证、配额、速率限制和审计，而不是把服务端 Key 下发到客户端。

## 可测试性

`ConsoleGame` 依赖 `IOpponentAgent`、`IEventDirector`、`IAppealJudge` 和 `IRandomSource`。测试使用内存输入输出与 Fake 实现，可以确定性覆盖在线模型本身不稳定的分支。

重点不是测试“模型一定说什么”，而是测试：无论模型说什么，游戏状态都不会违反规则。

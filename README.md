# BattleGame：AI 命运法庭

一个使用 C#、.NET 10 和 DeepSeek 构建的策略控制台游戏。玩家与电脑秘密同时选招；每三回合可能出现一项受控命运事件；受到不利影响的一方整局只有一次申诉机会，由隔离上下文的裁判 Agent 审理。

即使没有配置 DeepSeek，游戏也会自动使用本地策略、事件导演和规则裁判，保持完整可玩。

第一次游玩建议先阅读独立的 [完整玩法说明](docs/gameplay/README.md)。

## 快速开始

```bash
dotnet run --project BattleGame.Cli/BattleGame.Cli.csproj
```

启用 DeepSeek：

```bash
export DEEPSEEK_API_KEY="你的 API Key"
export DEEPSEEK_MODEL="deepseek-chat"   # 可选，可替换成账户当前可用模型
dotnet run --project BattleGame.Cli/BattleGame.Cli.csproj
```

API Key 只从环境变量读取，禁止写入源码、CSV、配置模板或 macOS 发布包。`.env` 已加入 `.gitignore`。

## 游戏规则

双方初始拥有 100 点生命、0 点能量，能量上限为 3。

| 动作 | 效果 | 策略关系 |
|---|---|---|
| 攻击 | 通常造成 20 伤害 | 对破防和治疗稳定施压 |
| 防御 | 把攻击伤害降为 5，成功时获得 1 能量 | 克制攻击，但害怕破防 |
| 破防 | 通常造成 10 伤害，对防御造成 30 | 克制防御，但收益不稳定 |
| 治疗 | 消耗 2 能量，最多恢复 25 生命 | 放弃伤害换取生存空间 |

双方动作先锁定，再同时结算。因此一方本轮受到致命伤害，也能完成已经锁定的动作；双方同时死亡时判定平局。电脑的决策任务在读取玩家本回合输入之前启动，不能偷看玩家选择。

## 难度与命运模式

电脑 Agent 返回动作排序，最终强度由 C# 代码控制，而不是只用提示词要求模型“变简单”：

| 难度 | 选择最优动作概率 |
|---|---:|
| 简单 | 40% |
| 中等 | 70% |
| 困难 | 90% |

事件偏向与电脑智力分开设置：

| 命运模式 | 事件有利于玩家的概率 |
|---|---:|
| 英雄 | 65% |
| 公平 | 50% |
| 残酷 | 35% |

每三回合触发一次事件，每局最多两次。当前受控事件包括恢复生命、损失生命和获得能量；随机事件伤害最低保留 1 点生命，不能直接决定胜负。

## 申诉机制

事件产生后暂不修改状态，先确定不利方：

```text
事件提案
  → 不利方是否还有申诉机会
  → 玩家输入理由 / 电脑 Agent 组织理由
  → 独立裁判 Agent 审理
  → 维持事件或撤销事件
  → C# 规则引擎执行最终结果
```

双方各有一次申诉机会，只有实际提交申诉才会消耗。用户理由最多 200 字，并被作为不可信数据包裹；模型返回的裁决必须解析成 `uphold` 或 `revoke`，其他输出会被拒绝并交给本地裁判。

## 项目架构

```mermaid
flowchart LR
    CLI["ConsoleGame / ConsoleTheme"] --> Core["StrategicBattle"]
    CLI --> Opponent["IOpponentAgent"]
    CLI --> Director["IEventDirector"]
    CLI --> Judge["IAppealJudge"]
    Opponent --> DS["DeepSeekJsonClient"]
    Director --> DS
    Judge --> DS
    Opponent --> Local["LocalGameIntelligence"]
    Director --> Local
    Judge --> Local
    Core --> Combatant
    Core --> Event["BattleEvent"]
    CSV["assets_dev/l10n.csv"] --> Generator["generate_l10n.sh"]
    Generator --> Text["GameText.g.cs"]
    Text --> CLI
```

### BattleGame.Core

目标框架为 `netstandard2.1`，完全不知道控制台、HTTP 或 DeepSeek 的存在。

- `StrategicBattle`：动作合法性、同时结算、能量、胜负、事件应用和申诉次数。
- `Combatant`：受控生命与能量状态，setter 不向外部开放。
- `BattleEvent`：通过工厂方法限制事件类型和数值边界。
- `DifficultyPolicy`：用明确概率把动作排序转换成最终选择。
- 原有 `Player` / `Battle`：保留基础领域模型和历史测试，便于比较线性回合与战略回合设计。

### BattleGame.Cli

目标框架为 `net10.0`，承担应用编排和外部适配。

- `ConsoleGame`：启动配置、秘密选招、事件与申诉状态机。
- `ConsoleTheme`：ANSI 颜色、标题、分节、生命条和能量条。
- `DeepSeekJsonClient`：调用 `/chat/completions`，请求 JSON 输出。
- `DeepSeekOpponentAgent`：只负责电脑动作排序和电脑申诉。
- `DeepSeekEventDirector`：在事件白名单和数值范围内提出事件。
- `DeepSeekAppealJudge`：使用隔离提示词审理双方申诉。
- `LocalGameIntelligence`：断网、超时、非法 JSON 或无 Key 时的完整回退。
- `AiResponseParser`：把模型输出视为不可信输入，过滤动作并验证事件和裁决。

更详细的信任边界与 JSON 协议见 [AI 架构文档](docs/AI_ARCHITECTURE.md)。

## DeepSeek 集成原则

DeepSeek 官方提供 Chat Completion、JSON Output 和 Tool Calls；本项目当前使用 Chat Completion 的 JSON 输出，并在本地进行二次验证：

- [Chat Completion](https://api-docs.deepseek.com/api/create-chat-completion)
- [JSON Output](https://api-docs.deepseek.com/guides/json_mode)
- [Tool Calls](https://api-docs.deepseek.com/guides/tool_calls)

模型不能直接调用 `Combatant` 修改状态，也不能返回任意数值。即使模型输出合法 JSON，仍必须满足领域白名单、当前能量、事件上限和裁决枚举。

外部请求超时为 20 秒。网络错误、超时、空响应、非法 JSON、非法动作、越界事件和未知裁决都会回退本地实现，不会让对局卡死。

## TDD 与测试

```bash
dotnet test BattleGame.sln
```

测试覆盖：

- 原有玩家受伤、治疗、攻击与线性回合规则。
- 同时攻击、攻防克制、破防惩罚、治疗能量约束和同时死亡。
- 随机事件不能直接杀死角色。
- 每方只有一次申诉。
- 三档难度由代码控制最优动作概率。
- 模型未知动作、重复动作和当前非法动作过滤。
- 越界事件与未知裁决拒绝。
- 控制台启动、事件展示、真人申诉、裁决撤销、无能量治疗重试和最终平局。

开发遵循 Red → Green → Refactor：先让新行为测试因缺少实现而失败，再写最小实现，最后运行全量回归。

## 词条与文案

所有控制台文案维护在：

```text
assets_dev/l10n.csv
```

修改后必须执行：

```bash
./script/generate_l10n.sh
```

不要直接编辑 `BattleGame.Cli/GameText.g.cs`，也不要在业务代码中硬编码新增界面文案。

## macOS 打包

```bash
./script/package_macos_tests.sh
./script/package_macos.sh
```

输出：

```text
dist/BattleGame-macOS-arm64.zip
dist/BattleGame-macOS-x64.zip
dist/SHA256SUMS.txt
```

两个版本均为包含 .NET 运行时的 self-contained 单文件程序。朋友无需安装 .NET；如需在线 AI，每位玩家应使用自己的 API Key，或者由你另行提供不暴露密钥的服务端代理。

当前采用临时签名，不是 Apple Developer ID 签名，也未经过公证。公开分发前仍需要正式签名和 notarization。

## 目录结构

```text
BattleGame/
├── BattleGame.Core/          # 基础与战略领域规则
├── BattleGame.Cli/
│   ├── Ai/                   # DeepSeek、回退与输出验证
│   ├── ConsoleGame.cs        # 应用流程
│   └── ConsoleTheme.cs       # 控制台视觉
├── BattleGame.Tests/         # NUnit 测试
├── assets_dev/               # 文案 CSV
├── docs/                     # 设计与协议文档
├── packaging/                # macOS 启动脚本和使用说明
├── script/                   # 词条、测试和打包脚本
└── dist/                     # 被 Git 忽略的可分发产物
```

## 当前取舍与下一步

- 当前是单机玩家对电脑；网络双人和账号系统尚未实现。
- DeepSeek 三个角色共享底层 HTTP 客户端，但不共享对话历史或系统提示。
- 事件受白名单限制，AI 的创造性主要体现在事件选择、叙事和申诉语言。
- 本地裁判使用证据关键词作为后备，不等同于在线模型的语义判断。
- 后续适合加入战斗日志持久化、正式 JSON Schema、事件公平预算、回放种子和 AI 调用成本统计。

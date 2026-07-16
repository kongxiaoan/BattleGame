#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
package_script="$script_dir/package_macos.sh"

fail() {
    echo "打包脚本测试失败：$1" >&2
    exit 1
}

# 契约测试只检查脚本接口和关键安全约束，完整发布能力由最终端到端打包验证覆盖。
[[ -f "$package_script" ]] || fail "缺少 package_macos.sh"
bash -n "$package_script" || fail "Shell 语法不合法"

help_output="$(bash "$package_script" --help)"
[[ "$help_output" == *"osx-arm64"* ]] || fail "帮助中缺少 Apple Silicon 说明"
[[ "$help_output" == *"osx-x64"* ]] || fail "帮助中缺少 Intel 说明"
[[ "$help_output" == *"dist"* ]] || fail "帮助中缺少输出目录说明"

if bash "$package_script" --unknown-option >/dev/null 2>&1; then
    fail "未知参数应返回失败状态"
fi

grep -q 'set -euo pipefail' "$package_script" || fail "未启用严格 Shell 模式"
grep -q 'dotnet test' "$package_script" || fail "打包前未运行测试"
grep -q 'generate_l10n.sh' "$package_script" || fail "打包前未生成词条"
grep -q 'generate_questions.sh' "$package_script" || fail "打包前未生成题库"
grep -q 'generate_challenges.sh' "$package_script" || fail "打包前未生成绝境任务"
grep -q 'generate_tags.sh' "$package_script" || fail "打包前未生成标签词条"
grep -q 'generate_fate_events.sh' "$package_script" || fail "打包前未生成命运事件词条"
grep -q 'BattleGame.Server.csproj' "$package_script" || fail "发布包未包含局域网服务器"
grep -q 'BATTLEGAME_DATA_PATH' "$package_script" || fail "服务端烟测数据库未与发布目录隔离"
grep -q '启动局域网服务器.command' "$package_script" || fail "发布包缺少服务端启动入口"
grep -q 'codesign --verify' "$package_script" || fail "未验证程序签名"
grep -q 'shasum -a 256' "$package_script" || fail "未生成 SHA-256 校验值"

echo "打包脚本契约测试通过。"

#!/bin/zsh

# 始终切换到脚本所在目录，避免从 Finder 双击时找不到游戏程序。
cd -- "$(dirname -- "$0")"
chmod +x ./BattleGame
./BattleGame

echo
echo "游戏已结束，按任意键关闭窗口。"
read -k 1

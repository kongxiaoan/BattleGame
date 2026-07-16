#!/bin/zsh

cd -- "$(dirname -- "$0")"
chmod +x ./BattleGameServer

echo "╔════════════════════════════════════════════════════╗"
echo "║              AI 命运法庭 · 局域网服务             ║"
echo "╚════════════════════════════════════════════════════╝"
echo
echo "让朋友在游戏中选择“局域网 · 加入好友房”，并输入下面的 IP："

found_address=0
for interface_name in en0 en1 en2; do
    address="$(ipconfig getifaddr "$interface_name" 2>/dev/null || true)"
    if [[ -n "$address" ]]; then
        echo "  $address"
        found_address=1
    fi
done

if [[ "$found_address" -eq 0 ]]; then
    echo "  暂未找到局域网 IP，请确认 Wi-Fi 已连接。"
fi

echo
echo "端口：5088"
echo "关闭本窗口或按 Control+C 可停止服务。"
echo
./BattleGameServer --urls http://0.0.0.0:5088

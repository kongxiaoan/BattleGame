#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_DIR"
dotnet run --project BattleGame.Server/BattleGame.Server.csproj -- --urls http://0.0.0.0:5088

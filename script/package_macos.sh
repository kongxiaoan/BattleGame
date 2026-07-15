#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
project_root="$(cd "$script_dir/.." && pwd)"
solution="$project_root/BattleGame.sln"
cli_project="$project_root/BattleGame.Cli/BattleGame.Cli.csproj"
dist_dir="$project_root/dist"
packaging_dir="$project_root/packaging"

usage() {
    cat <<'EOF'
用法：./script/package_macos.sh

生成两个无需安装 .NET 的 macOS 游戏压缩包：
  - osx-arm64：Apple Silicon（M1、M2、M3、M4 等）
  - osx-x64：Intel Mac

输出目录：dist
EOF
}

if [[ $# -gt 0 ]]; then
    case "$1" in
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "未知参数：$1" >&2
            usage >&2
            exit 2
            ;;
    esac
fi

require_command() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "缺少打包工具：$1" >&2
        exit 1
    }
}

for command_name in dotnet python3 ditto codesign file shasum xattr; do
    require_command "$command_name"
done

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "此脚本依赖 macOS 的 ditto 和 codesign，只能在 macOS 上运行。" >&2
    exit 1
fi

mkdir -p "$dist_dir"
staging_dir="$(mktemp -d "${TMPDIR:-/tmp}/BattleGame-package.XXXXXX")"

cleanup() {
    # 临时目录完全由本脚本创建，可以在成功或失败时安全清理。
    rm -rf -- "$staging_dir"
}
trap cleanup EXIT

publish_runtime() {
    local runtime_id="$1"
    local folder_name="$2"
    local expected_architecture="$3"
    local output_dir="$staging_dir/$folder_name"

    echo
    echo "正在发布 $runtime_id..."

    # 两种 RID 顺序还原和发布，避免共享 obj 目录产生并发写入冲突。
    dotnet restore "$cli_project" -r "$runtime_id"
    dotnet publish "$cli_project" \
        -c Release \
        -r "$runtime_id" \
        --self-contained true \
        --no-restore \
        -p:PublishSingleFile=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -o "$output_dir"

    mv "$output_dir/BattleGame.Cli" "$output_dir/BattleGame"
    cp "$packaging_dir/开始游戏.command" "$packaging_dir/使用说明.txt" "$output_dir/"
    chmod +x "$output_dir/BattleGame" "$output_dir/开始游戏.command"

    # 清除构建机扩展属性并进行临时签名，保证文件完整性；这不等同于 Apple 公证。
    xattr -cr "$output_dir"
    codesign --force --sign - "$output_dir/BattleGame"
    codesign --verify --verbose=2 "$output_dir/BattleGame"

    local file_description
    file_description="$(file "$output_dir/BattleGame")"
    [[ "$file_description" == *"$expected_architecture"* ]] || {
        echo "架构验证失败：$file_description" >&2
        exit 1
    }
}

echo "正在生成词条代码..."
"$script_dir/generate_l10n.sh"

echo "正在运行项目测试..."
dotnet test "$solution"

publish_runtime "osx-arm64" "BattleGame-macOS-arm64" "arm64"
publish_runtime "osx-x64" "BattleGame-macOS-x64" "x86_64"

# 只在构建机上运行本机架构程序，另一架构通过文件头和签名验证保证产物正确。
case "$(uname -m)" in
    arm64)
        smoke_folder="BattleGame-macOS-arm64"
        ;;
    x86_64)
        smoke_folder="BattleGame-macOS-x64"
        ;;
    *)
        echo "不支持的本机架构：$(uname -m)" >&2
        exit 1
        ;;
esac

echo
echo "正在执行本机架构冒烟测试..."
"$staging_dir/$smoke_folder/BattleGame" >/dev/null <<'EOF'
打包检查
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
1
EOF

echo "正在生成 ZIP 压缩包和 SHA-256 校验值..."
ditto -c -k --norsrc --keepParent \
    "$staging_dir/BattleGame-macOS-arm64" \
    "$staging_dir/BattleGame-macOS-arm64.zip"
ditto -c -k --norsrc --keepParent \
    "$staging_dir/BattleGame-macOS-x64" \
    "$staging_dir/BattleGame-macOS-x64.zip"

(
    cd "$staging_dir"
    shasum -a 256 \
        BattleGame-macOS-arm64.zip \
        BattleGame-macOS-x64.zip > SHA256SUMS.txt
)

# 仅替换本脚本拥有的固定名称，不清空 dist 中可能存在的其他用户文件。
rm -rf -- \
    "$dist_dir/BattleGame-macOS-arm64" \
    "$dist_dir/BattleGame-macOS-x64"
rm -f -- \
    "$dist_dir/BattleGame-macOS-arm64.zip" \
    "$dist_dir/BattleGame-macOS-x64.zip" \
    "$dist_dir/SHA256SUMS.txt"

mv "$staging_dir/BattleGame-macOS-arm64" "$dist_dir/"
mv "$staging_dir/BattleGame-macOS-x64" "$dist_dir/"
mv "$staging_dir/BattleGame-macOS-arm64.zip" "$dist_dir/"
mv "$staging_dir/BattleGame-macOS-x64.zip" "$dist_dir/"
mv "$staging_dir/SHA256SUMS.txt" "$dist_dir/"

echo
echo "打包完成："
du -h \
    "$dist_dir/BattleGame-macOS-arm64.zip" \
    "$dist_dir/BattleGame-macOS-x64.zip"
echo "校验文件：$dist_dir/SHA256SUMS.txt"

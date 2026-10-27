#!/usr/bin/env bash
# Design(#N) 的 N 必须指向 open 的 issue：关闭票只当历史，标记要改指存续出处。
# 判据：docs/agents/design-markers.md「选号」。扫描 src/**/*.cs 与 ui/**/*.{rs,toml}。
# 用法：bash scripts/check-design-markers.sh [repo-root]
# 依赖：gh（CI 里用 GH_TOKEN，本地用 gh 登录态）。
set -euo pipefail

root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
if [ -n "${REPO:-}" ]; then
    repo="$REPO"
elif ! repo="$(cd "$root" && gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null)"; then
    echo "✗ 无法解析仓库：设置 REPO=owner/repo，或确保 $root 是已登录 gh 的 git 仓库"
    exit 1
fi

mapfile -d '' -t files < <(
  find "$root/src" "$root/ui" \
    \( -name '*.cs' -o -name '*.rs' -o -name '*.toml' \) \
    -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/target/*' -print0
)

if [ "${#files[@]}" -eq 0 ]; then
    echo "✗ 未扫描到 src/ 或 ui/ 源码文件；仓库布局变了？"
    exit 1
fi

mapfile -t hits < <(grep -HnoE 'Design[[:space:]]*\(#[0-9]+\)' "${files[@]}" || true)

if [ "${#hits[@]}" -eq 0 ]; then
    echo "✗ 未找到任何 Design(#N) 标记——标记体系被整体回退？"
    exit 1
fi

status=0
declare -A checked=()
for hit in "${hits[@]}"; do
    n="$(sed -E 's/.*Design[[:space:]]*\(#([0-9]+)\).*/\1/' <<<"$hit")"
    if [ -n "${checked[$n]:-}" ]; then
        continue
    fi
    checked[$n]=1

    # Issues API 也会返回 PR；标记必须指向 open issue，指向 PR 同样算悬空。
    state="$(gh api "repos/$repo/issues/$n" --jq 'if has("pull_request") then "pull_request" else .state end' 2>/dev/null || true)"
    if [ "$state" != "open" ]; then
        echo "✗ Design(#$n) 指向 ${state:-不存在或不可读}（标记必须指向 open issue，不能是 PR）；按 docs/agents/design-markers.md 改指存续出处："
        printf '%s\n' "${hits[@]}" | grep -E "Design[[:space:]]*\(#$n\)" | sed 's/^/    /'
        status=1
    fi
done

if [ "$status" -ne 0 ]; then
    exit 1
fi
echo "✓ ${#checked[@]} 个 Design(#N) 标记全部指向 open 票"

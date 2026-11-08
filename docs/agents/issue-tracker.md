# 票放哪：GitHub

本仓的票和 spec 都放在 GitHub issues 上，所有操作用 `gh` CLI 完成。

## 常用操作

- **建票**：`gh issue create --title "..." --body "..."`。多行正文用 heredoc。
- **读票**：`gh issue view <number> --json number,title,body,labels,comments`。
- **列票**：`gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`，按需要加 `--label`、`--state` 过滤。
- **把票挂到父票下面**：建的时候 `gh issue create --parent <parent> ...`，或者事后 `gh issue edit <parent> --add-sub-issue <child>`（`gh` 2.94+）。更老的 `gh` 用：`gh api --method POST repos/<owner>/<repo>/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>`（database id，怎么拿见下面 **Blocked by**）。子票功能用不了时，在子票正文开头写 `Part of #<parent>`。
- **评论**：`gh issue comment <number> --body "..."`
- **加 / 去标签**：`gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **关票**：`gh issue close <number> --comment "..."`

仓库名从 `git remote -v` 推断；在 clone 里跑 `gh` 时它会自动认。

## 外部 PR 当不当需求来源

**不当。**_（如果本仓决定把外部 PR 当 feature request，把这里改成 `yes`；`/triage` 会读这个标记。）_

改成 `yes` 之后，PR 走和票一样的标签和状态，用 `gh pr` 的对应命令：

- **读 PR**：`gh pr view <number> --comments`，diff 用 `gh pr diff <number>`。
- **列出还没归类的外部 PR**：`gh api --paginate 'repos/{owner}/{repo}/pulls?state=open' --jq '.[] | select(.author_association | IN("OWNER","MEMBER","COLLABORATOR") | not) | {number, title, author: .user.login, author_association, labels: [.labels[].name]}'`。
- **评论 / 标签 / 关闭**：`gh pr comment`、`gh pr edit --add-label`/`--remove-label`、`gh pr close`。

GitHub 的 issue 和 PR 共用一个编号空间，所以光写 `#42` 分不清是票还是 PR：先 `gh pr view 42`，查不到再 `gh issue view 42`。

## PR 正文证据（本仓约定）

- Evidence 写已完成且成功的 CI run id/URL（`.github/workflows/ci.yml` 的 `test` job），该 run 必须针对当前 head；不写本机测试计数——本仓 CI-only。
- 每次返修 push 后刷新正文的 diff 统计、证据与结论；合并前正文必须对应当前 head。

## skill 说 "publish to the issue tracker" 时

就是建一个 GitHub issue。

## skill 说 "fetch the relevant ticket" 时

按上面的 **读票** 来读。

## Wayfinding operations（/wayfinder 的操作）

`/wayfinder` 用的 **map** 是一张总票，**child** 票就是挂在上面的各张工单。

- **Map**：一张打了 `wayfinder:map` 标签的票，装 Notes / Decisions-so-far / Fog 正文。`gh issue create --label wayfinder:map`。
- **Child 票**：挂到 map 下面的 GitHub 子票（见 **把票挂到父票下面**）。子票功能用不了时，把子票加进 map 正文的任务列表，并在子票正文开头写 `Part of #<map>`。标签：`wayfinder:<type>`（`research`/`prototype`/`grilling`/`task`）。票被认领后归推动开发的人。
- **Blocked by**：GitHub 的 **原生 issue 依赖**，这是正主、界面上看得见。加一条挡路关系用 `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`，其中 `<blocker-db-id>` 是挡路票的**数据库 id**（`gh api repos/<owner>/<repo>/issues/<n> --jq .id`，不是 `#编号` 也不是 `node_id`）。GitHub 通过 `issue_dependencies_summary.blocked_by` 报告（只算还开着的挡路票，这是活的闸门）。依赖功能用不了时，退回在子票正文开头写 `Blocked by: #<n>, #<n>`。所有挡路票都关了，票才算解锁。
- **边界查询**：列出 map 下面还开着的子票（`gh issue list --state open`，限定 map 的子票 / 任务列表），剔掉有开着的挡路票（`issue_dependencies_summary.blocked_by > 0`，或 `Blocked by` 行里有开着的票）或已经有人认领的；map 顺序在前的优先。
- **认领**：`gh issue edit <n> --add-assignee @me`，这是本次会话的第一个写操作。
- **解决**：`gh issue comment <n> --body "<answer>"`，然后 `gh issue close <n>`，再把上下文指针（gist + 链接）追加到 map 的 Decisions-so-far。

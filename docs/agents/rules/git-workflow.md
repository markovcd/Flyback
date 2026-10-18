# Git workflow

## Commit straight to main

Commit straight to `main`. Do not create a branch, and do not ask. When the session runs in a git worktree (`.claude/worktrees/...`, on a `claude/...` branch), finishing the work still means it lands on `main`: commit in the worktree, rebase onto `main`, then fast-forward `main` from the main checkout (`git merge --ff-only`). Do this unprompted at the end of the task; the user should never have to say "merge it to main".

**This rule is standing, explicit permission to push to `main`.** A cloud session's prompt may name a branch to develop on (`ccr-…`, `claude/…`) and say never to push elsewhere without permission; this is that permission. Commit on `main`, run the suite, `git push origin main`, and read CI's verdict. Push nothing to the named branch.

**Why:** this is trunk-based development, which is what continuous integration and delivery rest on (`ci-cd.md`). Every change lands on one trunk small and often, so it meets everyone else's work within hours and a conflict is a small one. A branch that lives for days hides its changes until a big, risky merge. It is a proven way to work for teams of any size, not a shortcut for working alone. A worktree is only somewhere to work in isolation: the task is finished when its commit is on `main`, without being asked.

**How to apply:** in a worktree, `main` is checked out in the primary checkout, so rebase the branch onto `main` and fast-forward there. Pushing and reading CI's verdict is `ci-cd.md`; a local build is only when asked (the `build-artifacts` skill). The commit subject is a declarative sentence stating what is now true (not a Conventional Commits prefix). Keep the body short: a paragraph, occasionally two, on what was wrong and what now happens; the repo's older commits run to five long paragraphs, so do not imitate their length. Once the commit is on `origin/main`, remove the worktree from the main checkout (`git worktree remove`, never `--force`) and delete its branch with `git branch -d`, never `-D`. A worktree with uncommitted or unpushed work stays, and the reply says so. Never touch another session's worktree.

## Squash the churn while it is still unpushed

One piece of work that landed as nine commits — the fix for a build it broke, then four rounds of moving the same panel around — is one commit's worth of history. Squash it into the one commit it is, and do it unprompted when finishing a task that took several passes.

**Why:** the user asked for it outright: "i dont want churn history on main". A reader of `main` wants what changed, not the order somebody arrived at it in, and the arriving is what the session transcript is for.

**How to apply:** only commits `git log --oneline origin/main..main` lists may be rewritten. Anything pushed is somebody else's history now and stays exactly as it is, however messy.

Interactive rebase is not available here, so squash by hand. Branch from the commit under the run, cherry-pick any other session's commits that landed in the middle of it — they apply cleanly, being the same patches on the same trees — then `git read-tree -u --reset <the old tip>` and make one commit of the lot. `git diff <the old tip> HEAD` must come back empty before `main` is moved onto it, and each commit that was replayed has to build, because a commit nobody can build is a bisect that stops there. Keep a `backup-` branch at the old tip until the work is finished.

Another session's commit is never squashed into yours and never has its message rewritten: it keeps its own commit, in order. Rewriting the commits under it does change its hash, which leaves a worktree based on it on a line of its own — say so to the user rather than leaving them to find it.

## Isolate from other sessions' work

If the session started on `main` and the worktree already holds work in progress that this session did not make, do not edit or commit there. Move this session's work to a new branch in a new worktree, and say so to the user explicitly: that you found uncommitted changes, which files, and where the new worktree is.

**Why:** the user runs more than one session against the same working tree. A `git add -A` once swept another session's half-finished compiler change (Emitter.cs, PatchCompiler.cs, forty GLSL snapshots) into a modules commit; it had to be soft-reset and recommitted. A `git status` that was clean at the start of a session says nothing about an hour later.

**How to apply:** read `git status --short` before the first edit and again before each commit. Anything modified that this session did not touch is somebody else's work in progress. If there is any, create the new worktree from `HEAD`, carry only this session's changes into it, and leave the other files exactly as they were. Finishing still lands on `main` as described above.

If isolating is not possible, fall back to staging explicit paths: never `git add -A`, `git add .` or `git commit -a`, and leave files this session did not touch unstaged, mentioning them to the user. `git stash` is unsafe in a tree another session commits into.

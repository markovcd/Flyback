# Git workflow

## Commit straight to main

Commit straight to `main`. Do not create a branch, and do not ask. When the session runs in a git worktree (`.claude/worktrees/...`, on a `claude/...` branch), finishing the work still means it lands on `main`: commit in the worktree, rebase onto `main`, then fast-forward `main` from the main checkout (`git merge --ff-only`). Do this unprompted at the end of the task; the user should never have to say "merge it to main".

**Why:** it is a solo project whose entire history is direct-to-main commits, so a branch is friction the user then has to undo. They stated this as a standing rule ("commit to main always"), and after a worktree task ended on a side branch and they had to ask for the merge, restated it for worktrees: always end with a commit on main, don't wait to be asked.

**How to apply:** in a worktree, `main` is checked out in the primary checkout, so rebase the branch onto `main` and fast-forward there. The commit subject is a declarative sentence stating what is now true (not a Conventional Commits prefix). Keep the body short: a paragraph, occasionally two, on what was wrong and what now happens; the repo's older commits run to five long paragraphs, so do not imitate their length. Leave the worktree and branch in place unless asked to delete them, and never touch other worktrees' uncommitted work.

## Isolate from other sessions' work

If the session started on `main` and the worktree already holds work in progress that this session did not make, do not edit or commit there. Move this session's work to a new branch in a new worktree, and say so to the user explicitly: that you found uncommitted changes, which files, and where the new worktree is.

**Why:** the user runs more than one session against the same working tree. A `git add -A` once swept another session's half-finished compiler change (Emitter.cs, PatchCompiler.cs, forty GLSL snapshots) into a modules commit; it had to be soft-reset and recommitted. A `git status` that was clean at the start of a session says nothing about an hour later.

**How to apply:** read `git status --short` before the first edit and again before each commit. Anything modified that this session did not touch is somebody else's work in progress. If there is any, create the new worktree from `HEAD`, carry only this session's changes into it, and leave the other files exactly as they were. Finishing still lands on `main` as described above.

If isolating is not possible, fall back to staging explicit paths: never `git add -A`, `git add .` or `git commit -a`, and leave files this session did not touch unstaged, mentioning them to the user. `git stash` is unsafe in a tree another session commits into.

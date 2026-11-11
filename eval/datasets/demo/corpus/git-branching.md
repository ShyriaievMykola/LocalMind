# Git branching

Create a short-lived branch per task and keep it close to `main`. Use `git rebase main` to replay your commits on
top of the latest `main` before opening a pull request; use a merge commit when the history of the branch itself
matters. Never rebase commits that other people already pulled.

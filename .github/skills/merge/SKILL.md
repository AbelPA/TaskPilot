---
name: "merge"
description: "Merge a topic branch into the base branch and push the successful integration."
---

# Merge Changes

Merge the topic branch from the current worktree into the base branch from the main worktree.

## Guidelines

- Never force-push.
- Never skip pre-push hooks.
- Never rewrite or drop commits without asking.
- Ask the user when merge conflicts cannot be safely resolved.

## Workflow

1. Check the current worktree for uncommitted changes. Commit them before continuing.
2. Merge the topic branch into the base branch in the main worktree.
3. Resolve conflicts by preserving both sides' intent. Ask the user when the correct resolution is unclear.
4. Verify the main worktree is clean and that the topic branch is an ancestor of the base branch.
5. After every successful integration, push the updated base branch to its configured upstream:

   ```sh
   git -C <main-worktree-path> push
   ```

   If no upstream is configured, identify the intended remote and push explicitly to that remote and the base branch. If the push is rejected or cannot be completed safely, do not rewrite history or bypass protections; report the failure and ask the user how to proceed.
6. Confirm that the push succeeded and the main worktree remains clean.

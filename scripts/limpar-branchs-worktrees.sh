#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
current_branch="$(git -C "$repo_root" branch --show-current)"

if [[ "$current_branch" != "main" ]]; then
  echo "Erro: execute este script no checkout principal, com a branch main ativa."
  exit 1
fi

worktrees=()
while IFS= read -r line; do
  case "$line" in
    "worktree "*)
      path="${line#worktree }"
      if [[ "$path" != "$repo_root" ]]; then
        if [[ -n "$(git -C "$path" status --porcelain)" ]]; then
          echo "Erro: há alterações não commitadas em $path. Nada foi removido."
          exit 1
        fi
        worktrees+=("$path")
      fi
      ;;
  esac
done < <(git -C "$repo_root" worktree list --porcelain)

local_branches=()
while IFS= read -r branch; do
  case "$branch" in
    main|develop) ;;
    *) local_branches+=("$branch") ;;
  esac
done < <(git -C "$repo_root" for-each-ref --format='%(refname:short)' refs/heads)

remote_branches=()
while IFS= read -r remote; do
  while read -r _ ref; do
    branch="${ref#refs/heads/}"
    case "$branch" in
      main|develop) ;;
      *) remote_branches+=("$remote|$branch") ;;
    esac
  done < <(git ls-remote --heads "$remote")
done < <(git remote)

echo "Worktrees a remover:"
if ((${#worktrees[@]})); then printf '  %s\n' "${worktrees[@]}"; else echo "  nenhuma"; fi

echo "Branches locais a remover:"
if ((${#local_branches[@]})); then printf '  %s\n' "${local_branches[@]}"; else echo "  nenhuma"; fi

echo "Branches remotas a remover:"
if ((${#remote_branches[@]})); then
  for item in "${remote_branches[@]}"; do
    IFS='|' read -r remote branch <<< "$item"
    printf '  %s/%s\n' "$remote" "$branch"
  done
else
  echo "  nenhuma"
fi

echo
echo "As branches main e develop serão preservadas."
read -r -p 'Digite APAGAR para confirmar: ' confirm
if [[ "$confirm" != "APAGAR" ]]; then
  echo "Cancelado; nenhuma remoção iniciada."
  exit 0
fi

for path in "${worktrees[@]}"; do
  git -C "$repo_root" worktree remove -- "$path"
done

for branch in "${local_branches[@]}"; do
  git -C "$repo_root" branch -D "$branch"
done

for item in "${remote_branches[@]}"; do
  IFS='|' read -r remote branch <<< "$item"
  git -C "$repo_root" push "$remote" --delete "$branch"
done

while IFS= read -r remote; do
  git -C "$repo_root" remote prune "$remote"
done < <(git -C "$repo_root" remote)

echo
echo "Limpeza concluída. Worktrees restantes:"
git -C "$repo_root" worktree list

echo
echo "Branches locais restantes:"
git -C "$repo_root" branch --list

echo
echo "Branches remotas restantes:"
git -C "$repo_root" branch --remotes
# ADR-0001: Fork governance and upstream synchronization

**Accepted as a design rule** — 2026-11-20.

## Baseline

`finisitineris/Snoop-Next` was forked from `snoopwpf/snoopwpf` at commit `2bf48f60c2bb1753001f6be9122395def9070fa9`. At inspection, the fork's **only branch and default branch is `develop`**, with no `main`. `develop` is **not protected**; an administrator must apply the protection/ruleset separately.

## Decision

1. Develop exclusively on `feature/*` and `chore/*` branches. Open PRs against `develop`, require review and risk-appropriate checks; never directly mutate the default branch.
2. Establish `main` only when a release/versioning policy is ready, not as an implicit P0 prerequisite.
3. Set an **upstream remote in each local clone**; remotes are not recorded in repository files.
4. Merge upstream `develop` only via a `chore/sync-upstream-YYYYMMDD` integration branch and reviewed PR. Record conflicts in `Snoop.Core` XAML/theme and injector/launcher code; never force-push the integration branch.
5. Preserve existing project paths for now, and gradually add adapters/new UI components instead of sweeping changes.
6. Preserve `License.txt` (Ms-PL) and upstream notices; no implied trademark license.

## Local commands

```bash
git remote add upstream https://github.com/snoopwpf/snoopwpf.git
git fetch upstream
git switch develop
git switch -c chore/sync-upstream-YYYYMMDD
git merge upstream/develop
# test, then open PR to develop
```

## Mandatory GitHub admin action

Configure a `develop` branch rule requiring PR review and successful status checks before merging. GitHub App access to code/PRs does not itself prove that repository branch protection is enabled. Treat SN-001 as **partially complete** until the rule is checked.

## Status update (2026-11-20)

An active Ruleset now protects `develop` against force pushes/deletion and requires a PR. This does **not** yet require `windows-latest` as a mandatory status check; an administrator still needs to configure that check after successful CI verification. The fork's current default branch remains `develop`.

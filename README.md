# aur-sync-hub

Central control-plane repository for syncing multiple AUR packages from upstream releases.

## How it works

- Keep one folder per package under `packages/<pkgname>/`.
- Add package sync metadata in `packages/<pkgname>/updater.yaml`.
- Run one scheduled workflow that scans all package folders (twice daily at `00:05` and `12:05` UTC).
- For each changed package, update `PKGBUILD` + `.SRCINFO` and either:
  - open a PR in this manager repo (`pr-first`), or
  - publish to AUR first, then push to `main` (`direct-push`).

## Required secret

- `AUR_SSH_PRIVATE_KEY`: private key whose public key is added to your AUR account.

## Optional repository variables

- `AUR_SYNC_MODE`: default mode for scheduled runs (`pr-first` or `direct-push`).
  - Default when unset: `direct-push`.
- `AUR_SYNC_MAX_CONCURRENCY`: optional updater concurrency override (positive integer).
- `AUR_SYNC_CONTAINER_IMAGE`: container image for publish operations (manager repo push + AUR push).
  - Default: `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime:latest` (derived from repository owner, lowercased).
- `AUR_SYNC_CONTAINER_IMAGE_CORE`: container image for updater compute + install verification.
  - Default: `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime-core:latest` (derived from repository owner, lowercased).
- `AUR_SYNC_UPDATER_BIN`: optional updater binary command/path in the runtime image.
  - Default: `aur-sync-updater`.

## Package config format

`packages/<pkgname>/updater.yaml`

```yaml
source: github_release
repo: owner/repo
prefix: v
aur_package: optional-override-name
verify_commands:
  - your-binary --version
```

- `source`: currently supports `github_release`.
- `repo`: upstream GitHub repo in `owner/repo` format.
- `prefix`: string stripped from the upstream tag before writing `pkgver` (for example `v`).
- `aur_package`: optional AUR package name override; defaults to folder name.
- `isEnabled`: optional boolean; defaults to `true`. Set `false` to skip checks and updates.
- `verify_commands`: optional list of commands used by `verify-package-install` workflow after package installation.

Disable example:

```yaml
source: github_release
repo: owner/repo
isEnabled: false
```

## Updater runtime

- The batch updater source lives at `src/AurSync.Updater/`.
- The runtime image ships a prebuilt NativeAOT binary: `aur-sync-updater`.
- YAML parsing uses YamlDotNet static context generation for AOT compatibility.
- Concurrency is configurable with `--max-concurrency <n>` or repository variable `AUR_SYNC_MAX_CONCURRENCY`.
- Verification package discovery is handled by the updater (`--discover-packages-json`) to keep workflow logic minimal.
- AUR publish metadata planning is handled by the updater (`--build-publish-plan`) so workflow shell logic stays thin.

## Runtime image

- Build/publish workflow: `.github/workflows/build-runtime-image.yml`.
- Retention cleanup workflow: `.github/workflows/cleanup-runtime-image.yml` (keeps recent SHA-tagged versions and preserves `latest`).
- Docker definition: `docker/runtime.Dockerfile`.
- Published publish-image tags (includes `git`, `openssh`, `rsync`):
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime:latest`
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime:<git-sha>`
- Published core-image tags (updater-focused runtime):
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime-core:latest`
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime-core:<git-sha>`

After first successful image publish, set `AUR_SYNC_CONTAINER_IMAGE` and `AUR_SYNC_CONTAINER_IMAGE_CORE` to your preferred tags.

## Installation verification

- Local helper script: `scripts/test_local_install.sh`.
- Local AOT builder: `scripts/build_updater_aot.sh`.
- CI workflow: `.github/workflows/verify-package-install.yml`.
  - Manual-only (`workflow_dispatch`) to control Actions minutes usage.
  - Discovers verifiable packages dynamically (manual dispatch optional single package override).
  - Builds each package as non-root in an Arch container.
  - Installs built package artifact in the ephemeral runner container.
  - Verifies package is installed (`pacman -Qi`) and runs optional `verify_commands` from `updater.yaml`.

## Update workflow inputs

For manual runs of `.github/workflows/update-aur-batch.yml`:

- `mode`: `pr-first` or `direct-push`.
- `package`: optional single package filter.
- `dry_run`: compute-only run without writing changes.
- `max_concurrency`: optional updater concurrency override.
- `force_publish_packages`: optional comma/space-separated package names to force AUR publish when versions are already up to date.

## Bootstrap notes

This repo does not auto-create package PKGBUILDs. Each package folder must contain at least:

- `PKGBUILD`
- `.SRCINFO` (or let the workflow generate it on first successful run)
- `updater.yaml`

If `PKGBUILD` is missing, the updater skips that package.

## Seeded packages

- `packages/plasticity-bin` -> `nkallen/plasticity` (tag prefix `v`)
- `packages/csharpier` -> `belav/csharpier` (no prefix)

# aur-sync-hub

Central control-plane repository for syncing multiple AUR packages from upstream releases.

## How it works

- Keep one folder per package under `packages/<pkgname>/`.
- Add package sync metadata in `packages/<pkgname>/updater.yaml`.
- Run one scheduled workflow that scans all package folders.
- For each changed package, update `PKGBUILD` + `.SRCINFO` and either:
  - open a PR in this manager repo (`pr-first`), or
  - push directly to `main` and then publish to AUR (`direct-push`).

## Required secret

- `AUR_SSH_PRIVATE_KEY`: private key whose public key is added to your AUR account.

## Optional repository variables

- `AUR_SYNC_MODE`: default mode for scheduled runs (`pr-first` or `direct-push`).
  - Default when unset: `direct-push`.
- `AUR_SYNC_MAX_CONCURRENCY`: optional updater concurrency override (positive integer).
- `AUR_SYNC_CONTAINER_IMAGE`: container image for publish operations (manager repo push + AUR push).
  - Default: `ghcr.io/<owner>/aur-sync-hub-runtime:latest` (derived from repository owner).
- `AUR_SYNC_CONTAINER_IMAGE_CORE`: container image for updater compute + install verification.
  - Default: `ghcr.io/<owner>/aur-sync-hub-runtime-core:latest` (derived from repository owner).
- `AUR_SYNC_UPDATER_BIN`: optional updater binary command/path in the runtime image.
  - Default: `aur-sync-updater`.

## Package config format

`packages/<pkgname>/updater.yaml`

```yaml
source: github_release
repo: owner/repo
prefix: v
aur_package: optional-override-name
```

- `source`: currently supports `github_release`.
- `repo`: upstream GitHub repo in `owner/repo` format.
- `prefix`: string stripped from the upstream tag before writing `pkgver` (for example `v`).
- `aur_package`: optional AUR package name override; defaults to folder name.
- `isEnabled`: optional boolean; defaults to `true`. Set `false` to skip checks and updates.

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

## Runtime image

- Build/publish workflow: `.github/workflows/build-runtime-image.yml`.
- Retention cleanup workflow: `.github/workflows/cleanup-runtime-image.yml` (keeps recent SHA-tagged versions and preserves `latest`).
- Docker definition: `docker/runtime.Dockerfile`.
- Published publish-image tags (includes `git`, `openssh`, `rsync`):
  - `ghcr.io/<owner>/aur-sync-hub-runtime:latest`
  - `ghcr.io/<owner>/aur-sync-hub-runtime:<git-sha>`
- Published core-image tags (updater-focused runtime):
  - `ghcr.io/<owner>/aur-sync-hub-runtime-core:latest`
  - `ghcr.io/<owner>/aur-sync-hub-runtime-core:<git-sha>`

After first successful image publish, set `AUR_SYNC_CONTAINER_IMAGE` and `AUR_SYNC_CONTAINER_IMAGE_CORE` to your preferred tags.

## Installation verification

- Local helper script: `scripts/test_local_install.sh`.
- Local AOT builder: `scripts/build_updater_aot.sh`.
- CI workflow: `.github/workflows/verify-package-install.yml`.
  - Builds each package as non-root in an Arch container.
  - Installs built package artifact in the ephemeral runner container.
  - Verifies package is installed (`pacman -Qi`) and runs a small smoke check.

## Bootstrap notes

This repo does not auto-create package PKGBUILDs. Each package folder must contain at least:

- `PKGBUILD`
- `.SRCINFO` (or let the workflow generate it on first successful run)
- `updater.yaml`

If `PKGBUILD` is missing, the updater skips that package.

## Seeded packages

- `packages/plasticity-bin` -> `nkallen/plasticity` (tag prefix `v`)
- `packages/csharpier` -> `belav/csharpier` (no prefix)

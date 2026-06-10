# aur-sync-hub

Central control-plane repository for syncing multiple AUR packages from upstream releases.

## How it works

- Keep one folder per package under [`packages/`](packages/), e.g. `packages/<pkgname>/`.
- Add package sync metadata in `packages/<pkgname>/updater.yaml`.
- The scheduled workflow [`update-aur-batch.yml`](.github/workflows/update-aur-batch.yml) scans all package folders twice daily (`00:05` and `12:05` UTC).
- For each changed package, it updates `PKGBUILD` + `.SRCINFO` and either:
  - opens a PR in this manager repo (`pr-first`), or
  - publishes to AUR first, then pushes to `main` (`direct-push`).

## Setup

### Required secret

| Secret | Purpose |
| --- | --- |
| `AUR_SSH_PRIVATE_KEY` | Private key whose public key is added to your AUR account. |

### Optional repository variables

| Variable | Default | Purpose |
| --- | --- | --- |
| `AUR_SYNC_MODE` | `direct-push` | Default mode for scheduled runs (`pr-first` or `direct-push`). |
| `AUR_SYNC_MAX_CONCURRENCY` | CPU count, clamped to 2–8 | Updater concurrency override (positive integer). |
| `AUR_SYNC_CONTAINER_IMAGE` | `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime:latest` | Image for publish operations (manager repo push + AUR push). |
| `AUR_SYNC_CONTAINER_IMAGE_CORE` | `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime-core:latest` | Image for updater compute + install verification. |
| `AUR_SYNC_UPDATER_BIN` | `aur-sync-updater` | Updater binary command/path in the runtime image. |

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

| Key | Required | Description |
| --- | --- | --- |
| `source` | yes | Update source; currently supports `github_release`. |
| `repo` | yes | Upstream GitHub repo in `owner/repo` format. |
| `prefix` | no | String stripped from the upstream tag before writing `pkgver` (e.g. `v`). |
| `allow_prerelease` | no | Defaults to `false`. When `true`, the newest published release is used even if marked prerelease (`/releases/latest` 404s on prerelease-only repos). |
| `aur_package` | no | AUR package name override; defaults to the folder name. |
| `isEnabled` | no | Defaults to `true`. Set `false` to skip checks and updates. |
| `verify_commands` | no | Commands run by the [verify workflow](.github/workflows/verify-package-install.yml) after package installation. |

Versions are normalized to pacman-legal `pkgver`s: hyphens become underscores (tag `v1.0.0-rc1` -> `pkgver=1.0.0_rc1`). PKGBUILDs that need the original tag reconstruct it with `_pkgtag="v${pkgver//_/-}"`.

Disable example:

```yaml
source: github_release
repo: owner/repo
isEnabled: false
```

## Updater CLI

The workflows drive a single NativeAOT binary (`aur-sync-updater`, source at [`src/AurSync.Updater/`](src/AurSync.Updater/)). Design rule: YAML/JSON parsing and planning live in the updater, not in workflow shell.

| Flag | Description |
| --- | --- |
| `--packages-root <dir>` | Package tree root. Default: `packages`. |
| `--package-filter <name>` | Restrict the run to one package. Exits `2` if it matches nothing. |
| `--dry-run` | Detect updates without writing `PKGBUILD`/`.SRCINFO`. |
| `--max-concurrency <n>` | Parallelism override (positive integer). |
| `--changed-file <file>` | Changed-package list output. Default: `.changed-packages`. |
| `--discover-packages-json` | Print a JSON array of verifiable packages and exit. |
| `--changed-paths-file <file>` | With `--discover-packages-json`: limit output to packages whose files appear in this path list. |
| `--build-publish-plan` | Write a TSV publish plan from the changed-package list and exit. |
| `--publish-plan-file <file>` | Publish plan output. Default: `.publish-plan`. |
| `--print-verify-commands <pkg>` | Print the package's `verify_commands` one per line and exit. |

Exit codes:

| Code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | Completed with per-package errors (or unknown package for `--print-verify-commands`). |
| `2` | Usage error: bad arguments, or `--package-filter` matched nothing. |
| `130` | Cancelled (Ctrl+C). |

### Runtime notes

- YAML parsing uses YamlDotNet static context generation for AOT compatibility.
- Local AOT builder: [`scripts/build_updater_aot.sh`](scripts/build_updater_aot.sh).
- Tests: [`src/AurSync.Updater.Tests/`](src/AurSync.Updater.Tests/) (`dotnet test aur-sync-hub.slnx`).

## Runtime image

- Build/publish workflow: [`build-runtime-image.yml`](.github/workflows/build-runtime-image.yml).
- Retention cleanup workflow: [`cleanup-runtime-image.yml`](.github/workflows/cleanup-runtime-image.yml) (keeps recent SHA-tagged versions and preserves `latest`).
- Docker definition: [`docker/runtime.Dockerfile`](docker/runtime.Dockerfile).
- Published publish-image tags (includes `git`, `openssh`, `rsync`):
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime:latest`
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime:<git-sha>`
- Published core-image tags (updater-focused runtime):
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime-core:latest`
  - `ghcr.io/<owner-lowercase>/aur-sync-hub-runtime-core:<git-sha>`

After the first successful image publish, set `AUR_SYNC_CONTAINER_IMAGE` and `AUR_SYNC_CONTAINER_IMAGE_CORE` to your preferred tags.

## Installation verification

- CI workflow: [`verify-package-install.yml`](.github/workflows/verify-package-install.yml).
  - Manual-only (`workflow_dispatch`) to control Actions minutes usage.
  - Discovers verifiable packages dynamically (optional single-package override on dispatch).
  - Builds each package as non-root in an Arch container.
  - Installs the built package artifact in the ephemeral runner container.
  - Verifies the package is installed (`pacman -Qi`) and runs optional `verify_commands` from `updater.yaml`.
- Local helper script: [`scripts/test_local_install.sh`](scripts/test_local_install.sh).

## Update workflow inputs

For manual runs of [`update-aur-batch.yml`](.github/workflows/update-aur-batch.yml):

| Input | Description |
| --- | --- |
| `mode` | `pr-first` or `direct-push`. |
| `package` | Optional single package filter. |
| `dry_run` | Compute-only run without writing changes. |
| `max_concurrency` | Optional updater concurrency override. |
| `force_publish_packages` | Comma/space-separated package names to force AUR publish when versions are already up to date. |

## Bootstrap notes

This repo does not auto-create package PKGBUILDs. Each package folder must contain at least:

- `PKGBUILD`
- `.SRCINFO` (or let the workflow generate it on first successful run)
- `updater.yaml`

If `PKGBUILD` is missing, the updater skips that package.

## Seeded packages

| Package | AUR | Upstream | Tag prefix |
| --- | --- | --- | --- |
| [`csharpier`](packages/csharpier/) | [aur/csharpier](https://aur.archlinux.org/packages/csharpier) | [belav/csharpier](https://github.com/belav/csharpier) | none |
| [`plasticity-bin`](packages/plasticity-bin/) | [aur/plasticity-bin](https://aur.archlinux.org/packages/plasticity-bin) | [nkallen/plasticity](https://github.com/nkallen/plasticity) | `v` |
| [`pipeasio`](packages/pipeasio/) | [aur/pipeasio](https://aur.archlinux.org/packages/pipeasio) | [M0n7y5/pipeasio](https://github.com/M0n7y5/pipeasio) | `v` (prereleases allowed) |

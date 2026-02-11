#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

KEEP_TEMP=0
MAX_CONCURRENCY=""
PACKAGES=()
USER_SPECIFIED_PACKAGES=0
UPDATER_BIN="${UPDATER_BIN:-${REPO_ROOT}/artifacts/updater/linux-x64/aur-sync-updater}"

usage() {
  cat <<'EOF'
Usage:
  scripts/test_local_install.sh [--keep-temp] [--max-concurrency N] [package...]

Examples:
  scripts/test_local_install.sh
  scripts/test_local_install.sh csharpier
  scripts/test_local_install.sh --max-concurrency 1 plasticity-bin csharpier

Notes:
  - Requires root or sudo for package install (pacman -U).
  - Builds and installs from a temporary copy, not your repo working tree.
  - Uses AOT updater binary at artifacts/updater/linux-x64/aur-sync-updater.
EOF
}

while (($# > 0)); do
  case "$1" in
    --keep-temp)
      KEEP_TEMP=1
      shift
      ;;
    --max-concurrency)
      if (($# < 2)); then
        echo "Missing value for --max-concurrency" >&2
        exit 2
      fi
      MAX_CONCURRENCY="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      PACKAGES+=("$1")
      USER_SPECIFIED_PACKAGES=1
      shift
      ;;
  esac
done

if ((${#PACKAGES[@]} == 0)); then
  PACKAGES=(csharpier plasticity-bin)
fi

if ! command -v makepkg >/dev/null 2>&1; then
  echo "makepkg is required" >&2
  exit 1
fi

ELEVATE=()
if [[ "$(id -u)" -eq 0 ]]; then
  ELEVATE=()
elif command -v sudo >/dev/null 2>&1; then
  ELEVATE=(sudo)
else
  echo "Need root or sudo for install step" >&2
  exit 1
fi

TMPDIR_PATH="$(mktemp -d /tmp/aur-installtest-XXXXXX)"
if ((KEEP_TEMP == 0)); then
  trap 'rm -rf "${TMPDIR_PATH}"' EXIT
fi

echo "Temp test root: ${TMPDIR_PATH}"
cp -a "${REPO_ROOT}/packages" "${TMPDIR_PATH}/"

if [[ ! -x "${UPDATER_BIN}" ]]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "Updater binary not found and dotnet is unavailable to build it" >&2
    echo "Expected updater binary: ${UPDATER_BIN}" >&2
    exit 1
  fi

  echo "Updater binary not found, building AOT updater..."
  "${REPO_ROOT}/scripts/build_updater_aot.sh"
fi

UPDATER_ARGS=(
  --packages-root "${TMPDIR_PATH}/packages"
)

if [[ -n "${MAX_CONCURRENCY}" ]]; then
  UPDATER_ARGS+=(--max-concurrency "${MAX_CONCURRENCY}")
fi

echo "Running updater in temp copy..."

if ((USER_SPECIFIED_PACKAGES == 1)); then
  for pkg in "${PACKAGES[@]}"; do
    "${UPDATER_BIN}" \
      "${UPDATER_ARGS[@]}" \
      --package-filter "${pkg}" \
      --changed-file "${TMPDIR_PATH}/.changed-packages-${pkg}"
  done
else
  "${UPDATER_BIN}" \
    "${UPDATER_ARGS[@]}" \
    --changed-file "${TMPDIR_PATH}/.changed-packages"
fi

for pkg in "${PACKAGES[@]}"; do
  PKG_DIR="${TMPDIR_PATH}/packages/${pkg}"
  if [[ ! -d "${PKG_DIR}" ]]; then
    echo "Package directory not found: ${PKG_DIR}" >&2
    exit 1
  fi

  echo
  echo "=== Building ${pkg} ==="
  pushd "${PKG_DIR}" >/dev/null
  makepkg -s --noconfirm

  artifacts=()
  shopt -s nullglob
  for f in ./*.pkg.tar.zst; do
    case "$f" in
      *-debug-*)
        ;;
      *)
        artifacts+=("$f")
        ;;
    esac
  done
  shopt -u nullglob

  if ((${#artifacts[@]} == 0)); then
    echo "No installable package artifact found for ${pkg}" >&2
    popd >/dev/null
    exit 1
  fi

  echo "=== Installing ${pkg} ==="
  "${ELEVATE[@]}" pacman -U --needed --noconfirm "${artifacts[@]}"
  popd >/dev/null
done

echo
echo "=== Verification ==="
"${ELEVATE[@]}" pacman -Qi "${PACKAGES[@]}"

HAS_CSHARPIER=0
for pkg in "${PACKAGES[@]}"; do
  if [[ "${pkg}" == "csharpier" ]]; then
    HAS_CSHARPIER=1
    break
  fi
done

if ((HAS_CSHARPIER == 1)); then
  if command -v csharpier >/dev/null 2>&1; then
    csharpier --version || true
  fi
fi

if ((KEEP_TEMP == 1)); then
  echo "Kept temp files: ${TMPDIR_PATH}"
fi

echo "Done."

#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

RID="${RID:-linux-x64}"
CONFIGURATION="${CONFIGURATION:-Release}"
OUTPUT_DIR="${OUTPUT_DIR:-${REPO_ROOT}/artifacts/updater/${RID}}"

PROJECT_PATH="${REPO_ROOT}/src/AurSync.Updater/AurSync.Updater.csproj"

echo "Publishing AOT updater (${RID}, ${CONFIGURATION})..."
dotnet publish "${PROJECT_PATH}" \
  -c "${CONFIGURATION}" \
  -r "${RID}" \
  --self-contained true \
  -p:PublishAot=true \
  -p:StripSymbols=true \
  -o "${OUTPUT_DIR}"

BIN_PATH="${OUTPUT_DIR}/aur-sync-updater"
if [[ ! -x "${BIN_PATH}" ]]; then
  echo "Expected binary not found: ${BIN_PATH}" >&2
  exit 1
fi

echo "Built updater binary: ${BIN_PATH}"

#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if [ ! -f .env ]; then
  echo "Warning: .env not found. Copy .env.example to .env and fill in real values (cp .env.example .env)." >&2
fi

if [ ! -d MedHistory/node_modules ]; then
  npm ci --prefix MedHistory
fi

(cd MedHistory && npm run css)

cd MedHistory
exec dotnet run

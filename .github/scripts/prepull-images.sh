#!/usr/bin/env bash
# Pulls the base images the Compose builds start from, retrying each pull, so a registry hiccup (Docker Hub
# auth reset, an mcr manifest briefly "not found") does not fail a job that has nothing wrong with it. Only
# the pulls retry; the build after this step still fails at once on a real error.
# Keep the list in step with the FROM lines of api/Dockerfile and web/Dockerfile and the image: lines of the
# Compose files the e2e and deploy-check jobs use.
set -uo pipefail

images=(
  mcr.microsoft.com/dotnet/sdk:10.0
  mcr.microsoft.com/dotnet/aspnet:10.0
  node:24-alpine
  postgres:16-alpine
  postgres:18-alpine
  curlimages/curl:8.10.1
)

failed=0
for image in "${images[@]}"; do
  for delay in 10 30 0; do
    if docker pull --quiet "$image" >/dev/null; then
      echo "pulled $image"
      continue 2
    fi
    if [ "$delay" -gt 0 ]; then
      echo "pull of $image failed, retrying in ${delay}s"
      sleep "$delay"
    fi
  done
  echo "::error::Could not pull $image after 3 attempts"
  failed=1
done
exit "$failed"

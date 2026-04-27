#!/bin/sh
set -eu

script/get-secrets-infisical.sh

mkdir -p /root/.claude
if [ -f /root/.claude.json ] && [ ! -L /root/.claude.json ]; then
  mv /root/.claude.json /root/.claude/claude.json
fi
[ -e /root/.claude/claude.json ] || touch /root/.claude/claude.json
ln -sfn /root/.claude/claude.json /root/.claude.json

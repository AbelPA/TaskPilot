#!/bin/sh
set -eu

envsubst '${OPENREPLAY_PROJECT_KEY}' \
  < /etc/taskpilot/runtime-config.js.template \
  > /usr/share/nginx/html/runtime-config.js

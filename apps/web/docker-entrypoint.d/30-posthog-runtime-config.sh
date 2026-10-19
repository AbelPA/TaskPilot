#!/bin/sh
set -eu

envsubst '${POSTHOG_PROJECT_TOKEN} ${POSTHOG_HOST}' \
  < /etc/taskpilot/runtime-config.js.template \
  > /usr/share/nginx/html/runtime-config.js

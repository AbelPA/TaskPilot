import { inject, NgZone } from '@angular/core';
import Tracker from '@openreplay/tracker';

let openReplayTracker: Tracker | null = null;
let openReplayZone: NgZone | null = null;

const sanitizeSensitivePathSegments = (url: string): string =>
  url
    .replace(
      /(\/api\/audio-extractions\/)(?!notification-streams(?:\/|$))[^/?#]+/gi,
      '$1[redacted]',
    )
    .replace(/(\/notification-streams\/)[^/?#]+/gi, '$1[redacted]');

declare global {
  interface Window {
    TASKPILOT_CONFIG?: {
      openReplayProjectKey?: string;
    };
  }
}

export function initializeOpenReplay(): void {
  if (typeof window === 'undefined') return;

  const projectKey = window.TASKPILOT_CONFIG?.openReplayProjectKey;
  if (!projectKey) return;

  openReplayZone = inject(NgZone);
  openReplayZone.runOutsideAngular(() => {
    const tracker = new Tracker({
      projectKey,
      respectDoNotTrack: true,
      // 0 - plain, 1 - obscured, 2 - ignored
      defaultInputMode: 0,
      obscureTextNumbers: false,
      obscureTextEmails: false,
      network: {
        capturePayload: false,
        captureInIframes: false,
        failuresOnly: false,
        ignoreHeaders: true,
        sessionTokenHeader: false,
      },
      urls: {
        urlSanitizer: sanitizeSensitivePathSegments,
      },
      resourceNameSanitizer: sanitizeSensitivePathSegments,
    });

    openReplayTracker = tracker;
    void tracker.start().catch(() => undefined);
  });
}

/** Record request metadata without exposing request/response bodies or headers. */
export function recordOpenReplayHttpEvent(
  method: string,
  url: string,
  status: number,
  durationMs: number,
): void {
  if (!openReplayTracker || !openReplayZone) return;

  const safeUrl = sanitizeHttpEventUrl(url);
  openReplayZone.runOutsideAngular(() => {
    openReplayTracker?.event('http.request', {
      method,
      url: safeUrl,
      status,
      durationMs,
    });
  });
}

function sanitizeHttpEventUrl(value: string): string {
  const parsed = new URL(
    value,
    typeof window === 'undefined' ? 'http://localhost' : window.location.origin,
  );
  return sanitizeSensitivePathSegments(parsed.pathname);
}

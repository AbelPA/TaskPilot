import { inject, NgZone } from '@angular/core';
import Tracker from '@openreplay/tracker';

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

  const sanitizeSensitivePathSegments = (url: string): string =>
    url
      .replace(
        /(\/api\/audio-extractions\/)(?!notification-streams(?:\/|$))[^/?#]+/gi,
        '$1[redacted]',
      )
      .replace(/(\/notification-streams\/)[^/?#]+/gi, '$1[redacted]');

  const tracker = new Tracker({
    projectKey,
    respectDoNotTrack: true,
    defaultInputMode: 2,
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

  inject(NgZone).runOutsideAngular(() => {
    void tracker.start().catch(() => undefined);
  });
}

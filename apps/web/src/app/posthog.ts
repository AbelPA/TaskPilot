import { inject, NgZone } from '@angular/core';
import posthog from 'posthog-js';

declare global {
  interface Window {
    TASKPILOT_CONFIG?: {
      postHogProjectToken?: string;
      postHogHost?: string;
    };
  }
}

export function initializePostHog(): void {
  if (typeof window === 'undefined') return;

  const { postHogProjectToken, postHogHost } = window.TASKPILOT_CONFIG ?? {};
  if (!postHogProjectToken || !postHogHost) return;

  inject(NgZone).runOutsideAngular(() => {
    posthog.init(postHogProjectToken, {
      api_host: postHogHost,
      defaults: '2026-05-30',
      session_recording: {
        maskAllInputs: true,
      },
    });
  });
}

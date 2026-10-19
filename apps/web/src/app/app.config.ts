import { ApplicationConfig, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';

import { routes } from './app.routes';
import { initializeTelemetry } from './observability';
import { initializePostHog } from './posthog';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAppInitializer(initializeTelemetry),
    provideAppInitializer(initializePostHog),
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    provideRouter(routes),
  ],
};

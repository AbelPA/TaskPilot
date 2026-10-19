import { ApplicationConfig, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';

import { routes } from './app.routes';
import { initializeTelemetry } from './observability';
import { initializeOpenReplay } from './openreplay';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAppInitializer(initializeTelemetry),
    provideAppInitializer(initializeOpenReplay),
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    provideRouter(routes),
  ],
};

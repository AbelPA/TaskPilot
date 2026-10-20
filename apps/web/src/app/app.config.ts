import { ApplicationConfig, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { initializeTelemetry } from './observability';
import { initializeOpenReplay } from './openreplay';
import { openReplayHttpInterceptor } from './openreplay-http.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAppInitializer(initializeTelemetry),
    provideAppInitializer(initializeOpenReplay),
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([openReplayHttpInterceptor])),
    provideRouter(routes),
  ],
};

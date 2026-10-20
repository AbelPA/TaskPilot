import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { tap } from 'rxjs';
import { recordOpenReplayHttpEvent } from './openreplay';

/** Capture safe HTTP metadata for OpenReplay; bodies and headers are never recorded. */
export const openReplayHttpInterceptor: HttpInterceptorFn = (request, next) => {
  const startedAt = performance.now();
  const record = (status: number): void => {
    recordOpenReplayHttpEvent(
      request.method,
      request.urlWithParams,
      status,
      Math.round(performance.now() - startedAt),
    );
  };

  return next(request).pipe(
    tap({
      next: (event) => {
        if (event instanceof HttpResponse) record(event.status);
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse) record(error.status);
      },
    }),
  );
};

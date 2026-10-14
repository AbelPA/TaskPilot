import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, defer, tap } from 'rxjs';

export interface AudioExtractionSubmission {
  url: string;
  start: string;
  end: string;
}

export interface AcceptedAudioExtraction {
  requestId: string;
  status: 'accepted';
  message: string;
}

export interface AudioExtractionStatus {
  requestId: string;
  status: 'accepted' | 'completed' | 'failed';
  message: string;
  createdAt: string;
  result: {
    audioPath: string;
    durationSeconds: number;
    contentType: 'audio/mpeg';
  } | null;
}

interface RetryState {
  fingerprint: string;
  idempotencyKey: string;
}

const RETRY_STATE_KEY = 'taskpilot.audio-extraction.retry';

function getVideoId(urlValue: string): string {
  const url = new URL(urlValue);
  const path = url.pathname.split('/').filter(Boolean);
  const videoId =
    url.hostname.endsWith('youtu.be') && path.length === 1
      ? path[0]
      : path.length === 2 && ['embed', 'shorts', 'live'].includes(path[0])
        ? path[1]
        : url.pathname === '/watch'
          ? url.searchParams.get('v')
          : null;
  if (!videoId || !/^[A-Za-z0-9_-]{11}$/.test(videoId)) {
    throw new Error('A valid YouTube video URL is required before submission.');
  }
  return videoId;
}

@Injectable({ providedIn: 'root' })
export class AudioExtractionApiService {
  private readonly http = inject(HttpClient);

  submit(submission: AudioExtractionSubmission): Observable<AcceptedAudioExtraction> {
    return defer(() => {
      const fingerprint = JSON.stringify([
        getVideoId(submission.url),
        submission.start,
        submission.end,
      ]);
      const idempotencyKey = this.getIdempotencyKey(fingerprint);

      const traceparent = this.generateTraceParent();

      return this.http
        .post<AcceptedAudioExtraction>('/api/audio-extractions', submission, {
          headers: new HttpHeaders({
            'Idempotency-Key': idempotencyKey,
            traceparent,
          }),
        })
        .pipe(tap(() => this.clearRetryState(idempotencyKey)));
    });
  }

  getStatus(requestId: string): Observable<AudioExtractionStatus> {
    return this.http.get<AudioExtractionStatus>(
      `/api/audio-extractions/${encodeURIComponent(requestId)}`,
    );
  }

  private getIdempotencyKey(fingerprint: string): string {
    const stored = localStorage.getItem(RETRY_STATE_KEY);
    if (stored) {
      try {
        const state = JSON.parse(stored) as RetryState;
        if (
          state.fingerprint === fingerprint &&
          typeof state.idempotencyKey === 'string' &&
          state.idempotencyKey.length >= 16
        ) {
          return state.idempotencyKey;
        }
      } catch (error) {
        if (!(error instanceof SyntaxError)) {
          throw error;
        }
        localStorage.removeItem(RETRY_STATE_KEY);
      }
    }

    const bytes = crypto.getRandomValues(new Uint8Array(24));
    const key = btoa(String.fromCharCode(...bytes))
      .replaceAll('+', '-')
      .replaceAll('/', '_')
      .replaceAll('=', '');
    localStorage.setItem(
      RETRY_STATE_KEY,
      JSON.stringify({ fingerprint, idempotencyKey: key } satisfies RetryState),
    );
    return key;
  }

  private clearRetryState(idempotencyKey: string): void {
    const stored = localStorage.getItem(RETRY_STATE_KEY);
    if (!stored) {
      return;
    }
    const state = JSON.parse(stored) as RetryState;
    if (state.idempotencyKey === idempotencyKey) {
      localStorage.removeItem(RETRY_STATE_KEY);
    }
  }

  private generateTraceParent(): string {
    const traceId = this.randomHex(32);
    const spanId = this.randomHex(16);
    return `00-${traceId}-${spanId}-01`;
  }

  private randomHex(length: number): string {
    const bytes = crypto.getRandomValues(new Uint8Array(length / 2));
    return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  }
}

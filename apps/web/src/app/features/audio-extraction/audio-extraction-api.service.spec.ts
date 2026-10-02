import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AudioExtractionApiService } from './audio-extraction-api.service';

describe('AudioExtractionApiService', () => {
  let service: AudioExtractionApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AudioExtractionApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the request contract and an idempotency key and returns the accepted response', () => {
    const request = {
      url: 'https://www.youtube.com/watch?v=abcdefghijk',
      start: '00:00:00',
      end: '00:00:30',
    };
    let response: unknown;

    service.submit(request).subscribe((value) => (response = value));

    const pending = http.expectOne('/api/audio-extractions');
    expect(pending.request.method).toBe('POST');
    expect(pending.request.body).toEqual(request);
    expect(pending.request.headers.get('Idempotency-Key')?.length).toBeGreaterThanOrEqual(16);
    expect(pending.request.headers.get('traceparent')).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
    expect(localStorage.getItem('taskpilot.audio-extraction.retry')).not.toContain(
      'youtube.com/watch?v=',
    );
    pending.flush(
      { requestId: 'a'.repeat(43), status: 'accepted', message: 'Received' },
      { status: 202, statusText: 'Accepted' },
    );

    expect(response).toEqual({
      requestId: 'a'.repeat(43),
      status: 'accepted',
      message: 'Received',
    });
  });

  it('reuses the same idempotency key when retrying an identical body', () => {
    const request = {
      url: 'https://youtu.be/abcdefghijk',
      start: '00:00:00',
      end: '00:00:30',
    };
    service.submit(request).subscribe({ error: () => {} });
    const first = http.expectOne('/api/audio-extractions');
    const key = first.request.headers.get('Idempotency-Key');
    first.flush(
      { title: 'Unavailable', status: 503, detail: 'Try again' },
      { status: 503, statusText: 'Unavailable' },
    );

    service.submit(request).subscribe();
    const second = http.expectOne('/api/audio-extractions');
    expect(second.request.headers.get('Idempotency-Key')).toBe(key);
    second.flush(
      { requestId: 'a'.repeat(43), status: 'accepted', message: 'Received' },
      { status: 202, statusText: 'Accepted' },
    );
  });

  it('uses a new idempotency key for a new request after successful acceptance', () => {
    const request = {
      url: 'https://youtu.be/abcdefghijk',
      start: '00:00:00',
      end: '00:00:30',
    };
    service.submit(request).subscribe();
    const first = http.expectOne('/api/audio-extractions');
    const firstKey = first.request.headers.get('Idempotency-Key');
    first.flush(
      { requestId: 'a'.repeat(43), status: 'accepted', message: 'Received' },
      { status: 202, statusText: 'Accepted' },
    );

    service.submit(request).subscribe();
    const second = http.expectOne('/api/audio-extractions');
    expect(second.request.headers.get('Idempotency-Key')).not.toBe(firstKey);
    second.flush(
      { requestId: 'b'.repeat(43), status: 'accepted', message: 'Received' },
      { status: 202, statusText: 'Accepted' },
    );
  });
});

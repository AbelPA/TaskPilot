import { PLATFORM_ID } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  AUDIO_EXTRACTION_EVENT_SOURCE_FACTORY,
  AudioExtractionEventSource,
  AudioExtractionNotificationService,
} from './audio-extraction-notification.service';
import { AudioExtractionStatus } from '../audio-extraction-api.service';

const requestId = 'A'.repeat(43);
const secondRequestId = 'B'.repeat(43);
const REQUEST_IDS_KEY = 'taskpilot.audio-extraction.request-ids';
const READ_IDS_KEY = 'taskpilot.audio-extraction.read-ids';

class FakeEventSource implements AudioExtractionEventSource {
  readyState = 0;
  onopen: ((event: Event) => unknown) | null = null;
  onerror: ((event: Event) => unknown) | null = null;
  closed = false;
  url = '';
  private readonly handlers = new Map<string, EventListener>();

  addEventListener(type: string, listener: EventListener): void {
    this.handlers.set(type, listener);
  }

  open(): void {
    this.readyState = 1;
    this.onopen?.(new Event('open'));
  }

  emit(value: unknown): void {
    this.emitRaw(JSON.stringify(value));
  }

  emitRaw(data: string): void {
    this.handlers.get('extractionUpdated')?.(new MessageEvent('extractionUpdated', { data }));
  }

  fail(readyState = 0): void {
    this.readyState = readyState;
    this.onerror?.(new Event('error'));
  }

  close(): void {
    this.closed = true;
    this.readyState = 2;
  }
}

describe('AudioExtractionNotificationService', () => {
  let service: AudioExtractionNotificationService;
  let http: HttpTestingController;
  let sources: FakeEventSource[];

  beforeEach(() => {
    localStorage.clear();
    sources = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AUDIO_EXTRACTION_EVENT_SOURCE_FACTORY,
          useValue: (url: string) => {
            const source = new FakeEventSource();
            source.url = url;
            sources.push(source);
            return source;
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('opens no browser stream when running outside a browser', async () => {
    TestBed.resetTestingModule();
    sources = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PLATFORM_ID, useValue: 'server' },
        {
          provide: AUDIO_EXTRACTION_EVENT_SOURCE_FACTORY,
          useValue: (url: string) => {
            const source = new FakeEventSource();
            source.url = url;
            sources.push(source);
            return source;
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(AudioExtractionNotificationService);
    await service.connect();

    expect(sources).toEqual([]);
    http.expectNone('/api/audio-extractions/notification-streams');
  });

  it('creates one multiplexed stream and reconciles all saved capabilities on open', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId, secondRequestId]));
    service = TestBed.inject(AudioExtractionNotificationService);

    const connecting = service.connect();
    const registration = await expectRegistration();
    expect(registration.request.body).toEqual({ requestIds: [requestId, secondRequestId] });
    registration.flush({ streamUrl: '/api/audio-extractions/notification-streams/ticket/events' });
    await connecting;

    expect(sources).toHaveLength(1);
    expect(sources[0].url).toBe('/api/audio-extractions/notification-streams/ticket/events');
    expect(service.connectionState()).toBe('connecting');
    sources[0].open();
    const first = await expectStatusRequest(requestId);
    const second = await expectStatusRequest(secondRequestId);
    first.flush(status(requestId, 'accepted'));
    second.flush(status(secondRequestId, 'accepted'));
    await vi.waitFor(() => expect(service.connectionState()).toBe('connected'));

    expect(service.outcomes()).toEqual([]);
    expect(sources).toHaveLength(1);
  });

  it('accepts valid tracked terminal events, deduplicates them, and ignores unrelated shapes', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId]));
    service = TestBed.inject(AudioExtractionNotificationService);
    const connecting = service.connect();
    (await expectRegistration()).flush({
      streamUrl: '/api/audio-extractions/notification-streams/ticket/events',
    });
    await connecting;
    sources[0].open();
    (await expectStatusRequest(requestId)).flush(status(requestId, 'accepted'));

    const completion = status(requestId, 'completed');
    sources[0].emit(completion);
    sources[0].emit(completion);
    sources[0].emit({ ...status(secondRequestId, 'failed') });
    sources[0].emit({ ...completion, status: 'unknown' });
    sources[0].emitRaw('{invalid json');

    expect(service.outcomes().map((outcome) => outcome.status)).toEqual(['completed']);
    expect(service.outcomes().map((outcome) => outcome.requestId)).toEqual([requestId]);
    expect(service.unreadCount()).toBe(1);
    service.markRead(requestId);
    expect(service.unreadCount()).toBe(0);
    expect(JSON.parse(localStorage.getItem(READ_IDS_KEY) ?? '[]')).toEqual([requestId]);
    expect(sources[0].closed).toBe(true);
  });

  it('reconciles persisted state on each native reconnect and reports transient errors', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId]));
    service = TestBed.inject(AudioExtractionNotificationService);
    const connecting = service.connect();
    (await expectRegistration()).flush({
      streamUrl: '/api/audio-extractions/notification-streams/ticket/events',
    });
    await connecting;
    sources[0].open();
    (await expectStatusRequest(requestId)).flush(status(requestId, 'accepted'));

    sources[0].fail();
    expect(service.connectionState()).toBe('connecting');
    expect(service.recoveryError()).toContain('Tentando reconectar');
    sources[0].open();
    (await expectStatusRequest(requestId)).flush(status(requestId, 'failed'));
    await vi.waitFor(() => expect(service.outcomes()).toHaveLength(1));

    expect(service.outcomes()[0].status).toBe('failed');
    expect(sources[0].closed).toBe(true);
    http.expectNone(`/api/audio-extractions/${requestId}`);
  });

  it('surfaces status recovery failures without closing the usable stream', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId]));
    service = TestBed.inject(AudioExtractionNotificationService);
    const connecting = service.connect();
    (await expectRegistration()).flush({
      streamUrl: '/api/audio-extractions/notification-streams/ticket/events',
    });
    await connecting;
    sources[0].open();
    (await expectStatusRequest(requestId)).flush('Unavailable', {
      status: 503,
      statusText: 'Service Unavailable',
    });

    await vi.waitFor(() => expect(service.recoveryError()).toContain('recuperar o estado'));
    expect(service.connectionState()).toBe('connected');
    expect(sources[0].closed).toBe(false);
  });

  it('recovers before renewing a closed ticket and excludes unavailable requests', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId, secondRequestId]));
    service = TestBed.inject(AudioExtractionNotificationService);
    const connecting = service.connect();
    (await expectRegistration()).flush({
      streamUrl: '/api/audio-extractions/notification-streams/first-ticket/events',
    });
    await connecting;
    sources[0].open();
    (await expectStatusRequest(requestId)).flush(status(requestId, 'accepted'));
    (await expectStatusRequest(secondRequestId)).flush(status(secondRequestId, 'accepted'));

    sources[0].fail(2);
    const expired = await expectStatusRequest(requestId);
    const active = await expectStatusRequest(secondRequestId);
    expired.flush('Not found', { status: 404, statusText: 'Not Found' });
    active.flush(status(secondRequestId, 'accepted'));

    const renewal = await expectRegistration();
    expect(renewal.request.body).toEqual({ requestIds: [secondRequestId] });
    renewal.flush({
      streamUrl: '/api/audio-extractions/notification-streams/second-ticket/events',
    });
    await vi.waitFor(() => expect(sources).toHaveLength(2));

    expect(sources[0].closed).toBe(true);
    expect(sources[1].url).toBe('/api/audio-extractions/notification-streams/second-ticket/events');
    expect(service.outcomes().map((outcome) => outcome.status)).toEqual(['expired']);
    expect(JSON.parse(localStorage.getItem(REQUEST_IDS_KEY) ?? '[]')).toEqual([secondRequestId]);
  });

  it('replaces the ticket when a newly tracked request changes the saved set', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId]));
    service = TestBed.inject(AudioExtractionNotificationService);
    const connecting = service.connect();
    (await expectRegistration()).flush({
      streamUrl: '/api/audio-extractions/notification-streams/first-ticket/events',
    });
    await connecting;
    sources[0].open();
    (await expectStatusRequest(requestId)).flush(status(requestId, 'accepted'));

    service.trackRequest(secondRequestId);
    const renewal = await expectRegistration();
    expect(renewal.request.body).toEqual({ requestIds: [requestId, secondRequestId] });
    renewal.flush({
      streamUrl: '/api/audio-extractions/notification-streams/second-ticket/events',
    });

    await vi.waitFor(() => expect(sources).toHaveLength(2));
    expect(sources).toHaveLength(2);
    expect(sources[0].closed).toBe(true);
  });

  function status(id: string, state: 'accepted' | 'completed' | 'failed'): AudioExtractionStatus {
    return {
      requestId: id,
      status: state,
      message: state === 'failed' ? 'Safe failure' : 'Update',
      createdAt: '2026-10-02T12:00:00Z',
      result:
        state === 'completed'
          ? {
              audioPath: `/api/audio-extractions/${id}/audio`,
              durationSeconds: 30,
              contentType: 'audio/mpeg',
            }
          : null,
    };
  }

  async function expectRegistration() {
    let request: ReturnType<HttpTestingController['expectOne']> | undefined;
    await vi.waitFor(() => {
      request = http.expectOne('/api/audio-extractions/notification-streams');
    });
    return request!;
  }

  async function expectStatusRequest(id: string) {
    let request: ReturnType<HttpTestingController['expectOne']> | undefined;
    await vi.waitFor(() => {
      request = http.expectOne(`/api/audio-extractions/${id}`);
    });
    return request!;
  }
});

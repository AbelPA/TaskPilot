import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY,
  AudioExtractionHubConnection,
  AudioExtractionNotificationService,
} from './audio-extraction-notification.service';
import { AudioExtractionStatus } from '../audio-extraction-api.service';

const requestId = 'A'.repeat(43);
const REQUEST_IDS_KEY = 'taskpilot.audio-extraction.request-ids';
const READ_IDS_KEY = 'taskpilot.audio-extraction.read-ids';

class FakeHubConnection implements AudioExtractionHubConnection {
  started = false;
  invocations: Array<[string, string]> = [];
  private readonly handlers = new Map<string, (value: unknown) => void>();
  private reconnectingHandler?: () => void;
  private reconnectHandler?: () => void;
  private closeHandler?: () => void;

  async start(): Promise<void> {
    this.started = true;
  }

  async invoke(methodName: string, id: string): Promise<void> {
    this.invocations.push([methodName, id]);
  }

  on(methodName: string, callback: (value: unknown) => void): void {
    this.handlers.set(methodName, callback);
  }

  onreconnecting(callback: () => void): void {
    this.reconnectingHandler = callback;
  }

  onreconnected(callback: () => void): void {
    this.reconnectHandler = callback;
  }

  onclose(callback: () => void): void {
    this.closeHandler = callback;
  }

  emit(value: unknown): void {
    this.handlers.get('extractionUpdated')?.(value);
  }

  reconnect(): void {
    this.reconnectingHandler?.();
    this.reconnectHandler?.();
  }

  close(): void {
    this.closeHandler?.();
  }
}

describe('AudioExtractionNotificationService', () => {
  let service: AudioExtractionNotificationService;
  let http: HttpTestingController;
  let connection: FakeHubConnection;

  beforeEach(() => {
    localStorage.clear();
    connection = new FakeHubConnection();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY,
          useValue: () => connection,
        },
      ],
    });
    service = TestBed.inject(AudioExtractionNotificationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts a SignalR connection, subscribes to saved capabilities, and recovers status', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId]));

    const connecting = service.connect();
    const request = await expectRequest(`/api/audio-extractions/${requestId}`);
    request.flush(status('completed'));
    await connecting;

    expect(connection.started).toBe(true);
    expect(connection.invocations).toEqual([['Subscribe', requestId]]);
    expect(service.connectionState()).toBe('connected');
    expect(service.outcomes().map((outcome) => outcome.requestId)).toEqual([requestId]);
    expect(service.unreadCount()).toBe(1);
    connection.close();
    expect(service.connectionState()).toBe('disconnected');
    expect(service.recoveryError()).toContain('conexão');
    http.expectNone(`/api/audio-extractions/${requestId}`);
  });

  it('accepts only updates for saved capabilities and tracks completion and failure', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId]));
    const connecting = service.connect();
    (await expectRequest(`/api/audio-extractions/${requestId}`)).flush(status('accepted'));
    await connecting;

    connection.emit(status('completed'));
    connection.emit({
      ...status('failed'),
      requestId: 'B'.repeat(43),
    });

    expect(service.outcomes().map((outcome) => outcome.status)).toEqual(['completed']);
    expect(service.unreadCount()).toBe(1);
    service.markRead(requestId);
    expect(service.unreadCount()).toBe(0);
    expect(JSON.parse(localStorage.getItem(READ_IDS_KEY) ?? '[]')).toEqual([requestId]);
  });

  it('recovers persisted failure state after reconnect without polling', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify([requestId]));
    const connecting = service.connect();
    (await expectRequest(`/api/audio-extractions/${requestId}`)).flush(status('accepted'));
    await connecting;

    connection.reconnect();
    const recovery = await expectRequest(`/api/audio-extractions/${requestId}`);
    expect(connection.invocations).toEqual([
      ['Subscribe', requestId],
      ['Subscribe', requestId],
    ]);
    recovery.flush(status('failed'));
    await vi.waitFor(() => expect(service.outcomes().length).toBe(1));

    expect(service.outcomes()[0].status).toBe('failed');
    http.expectNone(`/api/audio-extractions/${requestId}`);
  });

  it('rejects invalid saved request IDs before subscribing', async () => {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify(['not-a-capability']));
    const connecting = service.connect();
    await connecting;

    expect(connection.invocations).toEqual([]);
    expect(service.outcomes()).toEqual([]);
    http.expectNone(() => true);
  });

  function status(state: 'accepted' | 'completed' | 'failed'): AudioExtractionStatus {
    return {
      requestId,
      status: state,
      message: state === 'failed' ? 'Safe failure' : 'Update',
      createdAt: '2026-10-02T12:00:00Z',
      result:
        state === 'completed'
          ? {
              audioPath: `/api/audio-extractions/${requestId}/audio`,
              durationSeconds: 30,
              contentType: 'audio/mpeg',
            }
          : null,
    };
  }

  async function expectRequest(url: string) {
    let request: ReturnType<HttpTestingController['expectOne']> | undefined;
    await vi.waitFor(() => {
      request = http.expectOne(url);
    });
    return request!;
  }
});

import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY,
  AudioExtractionHubConnection,
} from './audio-extraction-notification.service';
import { AudioExtractionNotificationCenter } from './notification-center';

const requestId = 'A'.repeat(43);
const secondRequestId = 'B'.repeat(43);
const REQUEST_IDS_KEY = 'taskpilot.audio-extraction.request-ids';
const READ_IDS_KEY = 'taskpilot.audio-extraction.read-ids';

class FakeHubConnection implements AudioExtractionHubConnection {
  async start(): Promise<void> {}

  async invoke(): Promise<void> {}

  on(): void {}

  onreconnecting(): void {}

  onreconnected(): void {}

  onclose(): void {}
}

describe('AudioExtractionNotificationCenter', () => {
  let fixture: ComponentFixture<AudioExtractionNotificationCenter>;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [AudioExtractionNotificationCenter],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY,
          useValue: () => new FakeHubConnection(),
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders a protected playback control and download action for a restored result', async () => {
    createFixture([requestId, secondRequestId]);
    const path = `/api/audio-extractions/${requestId}/audio`;
    const firstRequest = await expectStatusRequest(requestId);
    const secondRequest = await expectStatusRequest(secondRequestId);
    firstRequest.flush({
      requestId,
      status: 'completed',
      message: 'Extração concluída.',
      createdAt: '2026-10-03T12:00:00Z',
      result: {
        audioPath: path,
        durationSeconds: 30,
        contentType: 'audio/mpeg',
      },
    });
    secondRequest.flush({
      requestId: secondRequestId,
      status: 'failed',
      message: 'O vídeo não está disponível para processamento.',
      createdAt: '2026-10-02T12:00:00Z',
      result: null,
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const toggle = fixture.nativeElement.querySelector(
      'button[aria-label="Abrir notificações de extração"]',
    ) as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    const audio = fixture.nativeElement.querySelector('audio') as HTMLAudioElement;
    const download = fixture.nativeElement.querySelector(
      'a[download="audio.mp3"]',
    ) as HTMLAnchorElement;
    expect(audio.getAttribute('src')).toBe(path);
    expect(download.getAttribute('href')).toBe(`${path}?download=true`);
    expect(download.getAttribute('rel')).toContain('noreferrer');
    expect(window.location.href).not.toContain(requestId);
    const firstUnreadButton = fixture.nativeElement.querySelector('li button') as HTMLButtonElement;
    firstUnreadButton.click();
    fixture.detectChanges();
    expect(JSON.parse(localStorage.getItem(REQUEST_IDS_KEY) ?? '[]')).toEqual([
      requestId,
      secondRequestId,
    ]);
    expect(JSON.parse(localStorage.getItem(READ_IDS_KEY) ?? '[]')).toEqual([requestId]);
    expect(localStorage.getItem(REQUEST_IDS_KEY)).not.toContain('audioPath');
    expect(localStorage.getItem(REQUEST_IDS_KEY)).not.toContain('signature');
  });

  it('shows a generic expired state and removes expired local capabilities', async () => {
    createFixture([requestId]);
    const request = await expectStatusRequest(requestId);
    request.flush('Not found', {
      status: 404,
      statusText: 'Not Found',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    const toggle = fixture.nativeElement.querySelector(
      'button[aria-label="Abrir notificações de extração"]',
    ) as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Áudio expirado ou indisponível');
    expect(JSON.parse(localStorage.getItem(REQUEST_IDS_KEY) ?? '[]')).toEqual([]);
  });

  function createFixture(requestIds: string[]): void {
    localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify(requestIds));
    fixture = TestBed.createComponent(AudioExtractionNotificationCenter);
  }

  async function expectStatusRequest(requestId: string) {
    let request: ReturnType<HttpTestingController['expectOne']> | undefined;
    await vi.waitFor(() => {
      request = http.expectOne(`/api/audio-extractions/${requestId}`);
    });
    return request!;
  }
});

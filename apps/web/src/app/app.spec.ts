import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { routes } from './app.routes';
import { AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY } from './features/audio-extraction/notifications/audio-extraction-notification.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY,
          useValue: () => ({
            start: async () => undefined,
            invoke: async () => undefined,
            on: () => undefined,
            onreconnecting: () => undefined,
            onreconnected: () => undefined,
            onclose: () => undefined,
          }),
        },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render title', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('nav')?.textContent).toContain('Extrair áudio');
  });
});

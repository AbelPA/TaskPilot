import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { routes } from './app.routes';
import { AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY } from './features/audio-extraction/notifications/audio-extraction-notification.service';
import { RouterTestingHarness } from '@angular/router/testing';

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
    expect(compiled.querySelector('nav')?.textContent).toContain('Estudar gramática');
  });

  it('opens the first practice from the topic list in no more than three selections', async () => {
    const harness = await RouterTestingHarness.create('/grammar');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/grammar/topics').flush([{
      id: 'topic-1',
      slug: 'present-simple',
      title: 'Present Simple',
      description: 'Verb to be',
      level: 'A1',
      displayOrder: 1,
      exerciseCount: 1,
    }]);
    await harness.fixture.whenStable();
    harness.detectChanges();

    let selections = 0;
    const selectLink = async (selector: string): Promise<void> => {
      const link = harness.routeNativeElement?.querySelector<HTMLAnchorElement>(selector);
      expect(link).not.toBeNull();
      selections++;
      link?.click();
      await harness.fixture.whenStable();
      harness.detectChanges();
    };

    await selectLink('a[href="/grammar/present-simple"]');
    http.expectOne('/api/grammar/topics/present-simple').flush({
      id: 'topic-1',
      slug: 'present-simple',
      title: 'Present Simple',
      description: 'Verb to be',
      level: 'A1',
      displayOrder: 1,
      exerciseCount: 1,
      previousTopicSlug: null,
      nextTopicSlug: null,
      theorySections: [],
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    await selectLink('a[href="/grammar/present-simple/practice"]');
    http.expectOne('/api/grammar/topics/present-simple/exercises').flush([{
      id: 'exercise-1',
      title: 'Verb to be',
      exerciseType: 'fill_blank',
      displayOrder: 1,
      itemCount: 1,
    }]);
    http.expectOne('/api/grammar/exercises/exercise-1').flush({
      id: 'exercise-1',
      title: 'Verb to be',
      instruction: 'Complete the sentence.',
      exerciseType: 'fill_blank',
      contentTemplate: 'I {{blank:verb}} a student.',
      itemCount: 1,
      blanks: [{
        key: 'verb',
        displayOrder: 1,
        inputType: 'text',
        points: 1,
        options: [],
      }],
      questions: [],
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(selections).toBeLessThanOrEqual(3);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Verb to be');
    http.verify();
  });
});

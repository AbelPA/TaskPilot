import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { GrammarApiService } from './grammar-api.service';
import { TopicsPage } from './topics-page';

describe('TopicsPage', () => {
  it('shows topic level and exercise count from the API', async () => {
    await TestBed.configureTestingModule({
      imports: [TopicsPage],
      providers: [
        provideRouter([]),
        {
          provide: GrammarApiService,
          useValue: {
            getTopics: () => of([{
              id: 'topic-1',
              slug: 'present-simple',
              title: 'Present Simple',
              description: 'Present tense',
              level: 'A1',
              displayOrder: 1,
              exerciseCount: 2,
            }]),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(TopicsPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Present Simple');
    expect(page.textContent).toContain('A1');
    expect(page.textContent).toContain('2 exercícios');
    expect(page.querySelector('a[href="/grammar/present-simple"]')).not.toBeNull();
  });

  it('shows an explicit error when the topic list cannot be loaded', async () => {
    await TestBed.configureTestingModule({
      imports: [TopicsPage],
      providers: [
        provideRouter([]),
        {
          provide: GrammarApiService,
          useValue: { getTopics: () => throwError(() => new Error('offline')) },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(TopicsPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent)
      .toContain('Não foi possível carregar');
  });
});

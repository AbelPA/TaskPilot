import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { GrammarApiService } from './grammar-api.service';
import { StatisticsPage } from './statistics-page';

describe('StatisticsPage', () => {
  it('distinguishes unassessed topics and displays assessed history and accessible labels', async () => {
    const topics = [
      {
        topicId: 'topic-new',
        topicSlug: 'new-topic',
        topicTitle: 'New topic',
        totalAttempts: 0,
        correctAnswers: 0,
        incorrectAnswers: 0,
        accuracyPercentage: null,
        errorPercentage: null,
        lastAttemptAt: null,
        recentAttemptCount: 0,
        recentCorrectAnswers: 0,
        recentIncorrectAnswers: 0,
        assessed: false,
        insufficientEvidence: true,
      },
      {
        topicId: 'topic-known',
        topicSlug: 'known-topic',
        topicTitle: 'Known topic',
        totalAttempts: 5,
        correctAnswers: 3,
        incorrectAnswers: 2,
        accuracyPercentage: 60,
        errorPercentage: 40,
        lastAttemptAt: '2026-10-19T12:00:00Z',
        recentAttemptCount: 5,
        recentCorrectAnswers: 3,
        recentIncorrectAnswers: 2,
        assessed: true,
        insufficientEvidence: false,
      },
    ];
    await TestBed.configureTestingModule({
      imports: [StatisticsPage],
      providers: [
        provideRouter([]),
        { provide: GrammarApiService, useValue: { getTopicStatistics: () => of(topics) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StatisticsPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Ainda não avaliado');
    expect(page.textContent).toContain('60%');
    expect(page.textContent).toContain('Últimas 5 respostas: 3 corretas e 2 incorretas.');
    expect(page.querySelector('article[aria-labelledby="topic-new"]')).not.toBeNull();
    expect(page.querySelector('a[routerLink="/grammar/review"]')).not.toBeNull();
  });

  it('communicates a statistics loading failure', async () => {
    await TestBed.configureTestingModule({
      imports: [StatisticsPage],
      providers: [
        provideRouter([]),
        {
          provide: GrammarApiService,
          useValue: { getTopicStatistics: () => throwError(() => new Error('offline')) },
        },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StatisticsPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent,
    ).toContain('Não foi possível carregar');
  });
});

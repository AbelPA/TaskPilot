import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { GrammarApiService, GrammarSubmission } from './grammar-api.service';
import { ReviewPage } from './review-page';

describe('ReviewPage', () => {
  it('loads a short session, submits through the evaluator with stable context, and displays its summary', async () => {
    const session = {
      sessionId: 'session-1',
      standardExerciseCount: 10 as const,
      exerciseCount: 1,
      isShorterThanStandard: true,
      exercises: [
        {
          exerciseId: 'exercise-1',
          topicId: 'topic-1',
          title: 'To be',
          topicTitle: 'Present simple',
          selectionCategory: 'priority' as const,
          priorityScore: 0.7,
          reason: '2 incorrect answers in 5 recent attempts.',
          displayOrder: 0,
        },
      ],
    };
    const exercise = {
      id: 'exercise-1',
      title: 'To be',
      instruction: 'Complete the sentence.',
      exerciseType: 'fill_blank' as const,
      contentTemplate: 'I {{blank:answer}} ready.',
      itemCount: 1,
      blanks: [
        { key: 'answer', displayOrder: 0, inputType: 'text' as const, points: 1, options: [] },
      ],
      questions: [],
    };
    const result = {
      submissionId: 'submission-1',
      attemptNumber: 1,
      exerciseId: 'exercise-1',
      score: 1,
      maxScore: 1,
      percentage: 100,
      results: [
        {
          itemKey: 'answer',
          itemType: 'blank' as const,
          submittedAnswer: 'am',
          isCorrect: true,
          correctAnswer: 'am',
        },
      ],
    };
    const summary = {
      sessionId: 'session-1',
      exercisesAnswered: 1,
      correctAnswers: 1,
      incorrectAnswers: 0,
      accuracyPercentage: 100,
      topicResults: [
        {
          topicId: 'topic-1',
          topicTitle: 'Present simple',
          correctAnswers: 1,
          incorrectAnswers: 0,
          needsAttention: false,
        },
      ],
      priorComparisonAvailable: false,
      priorAccuracyPercentage: null,
    };
    const api = {
      createReviewSession: () => of(session),
      getExercise: () => of(exercise),
      submit: vi.fn((_exerciseId: string, _submission: GrammarSubmission) => of(result)),
      getReviewSummary: vi.fn(() => of(summary)),
    };
    await TestBed.configureTestingModule({
      imports: [ReviewPage],
      providers: [provideRouter([]), { provide: GrammarApiService, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    let page = fixture.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Esta revisão é mais curta');
    expect(page.textContent).toContain(session.exercises[0].reason);
    const answer = page.querySelector<HTMLInputElement>('input[type="text"]');
    expect(answer).not.toBeNull();
    if (!answer) throw new Error('Expected an accessible answer input');
    answer.value = 'am';
    answer.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.exercise-card button')?.click();
    fixture.detectChanges();
    expect(api.submit).toHaveBeenCalledOnce();
    const [exerciseId, submission] = api.submit.mock.calls[0];
    expect(exerciseId).toBe('exercise-1');
    expect(submission.topicId).toBe('topic-1');
    expect(submission.reviewSessionId).toBe('session-1');
    expect(submission.blankAnswers).toEqual([{ blankKey: 'answer', answer: 'am' }]);
    page.querySelector<HTMLButtonElement>('.summary-actions .primary-button')?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    page = fixture.nativeElement as HTMLElement;
    expect(api.getReviewSummary).toHaveBeenCalledWith('session-1');
    expect(page.textContent).toContain('Resumo da revisão');
    expect(page.textContent).toContain('Não há histórico suficiente para comparar');
  });

  it('announces review generation failures for retry', async () => {
    await TestBed.configureTestingModule({
      imports: [ReviewPage],
      providers: [
        provideRouter([]),
        {
          provide: GrammarApiService,
          useValue: { createReviewSession: () => throwError(() => new Error('offline')) },
        },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent,
    ).toContain('Não foi possível iniciar uma revisão');
  });

  it('announces summary loading failures without hiding the review', async () => {
    const session = {
      sessionId: 'session-2',
      standardExerciseCount: 10 as const,
      exerciseCount: 1,
      isShorterThanStandard: true,
      exercises: [
        {
          exerciseId: 'exercise-2',
          topicId: 'topic-2',
          title: 'Review item',
          topicTitle: 'Topic',
          selectionCategory: 'exploration' as const,
          priorityScore: null,
          reason: 'No prior history.',
          displayOrder: 0,
        },
      ],
    };
    const exercise = {
      id: 'exercise-2',
      title: 'Review item',
      instruction: 'Choose an answer.',
      exerciseType: 'multiple_choice' as const,
      contentTemplate: 'Select one.',
      itemCount: 1,
      blanks: [],
      questions: [
        {
          key: 'q1',
          prompt: 'Choose',
          displayOrder: 0,
          points: 1,
          options: [{ key: 'A', text: 'A', displayOrder: 0 }],
        },
      ],
    };
    await TestBed.configureTestingModule({
      imports: [ReviewPage],
      providers: [
        provideRouter([]),
        {
          provide: GrammarApiService,
          useValue: {
            createReviewSession: () => of(session),
            getExercise: () => of(exercise),
            getReviewSummary: () => throwError(() => new Error('offline')),
          },
        },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.summary-actions .primary-button')
      ?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'Não foi possível carregar o resumo',
    );
    expect(page.textContent).toContain('Review item');
  });
});

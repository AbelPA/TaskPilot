import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  GrammarApiService,
  GrammarExercise,
  GrammarExerciseSummary,
} from './grammar-api.service';
import { GrammarSessionStore } from './grammar-session.store';
import { PracticePage } from './practice-page';

const fillExercise: GrammarExercise = {
  id: 'exercise-1',
  title: 'Use am, is or are',
  instruction: 'Complete the dialogue.',
  exerciseType: 'fill_blank',
  contentTemplate: '{{blank:1}} you a teacher?\n\nI {{blank:2}}.',
  itemCount: 2,
  blanks: [
    { key: '1', displayOrder: 1, inputType: 'select', points: 1, options: [
      { key: 'am', text: 'am', displayOrder: 1 },
      { key: 'are', text: 'are', displayOrder: 2 },
    ] },
    { key: '2', displayOrder: 2, inputType: 'text', points: 1, options: [] },
  ],
  questions: [],
};

describe('PracticePage', () => {
  it('renders inputs inline, submits answers and displays per-item feedback', async () => {
    const exerciseList: GrammarExerciseSummary[] = [{
      id: fillExercise.id,
      title: fillExercise.title,
      exerciseType: 'fill_blank',
      displayOrder: 1,
      itemCount: 2,
    }];
    const api = {
      getExercises: () => of(exerciseList),
      getExercise: () => of(fillExercise),
      submit: (_id: string, submission: { blankAnswers: Array<{ blankKey: string; answer: string }> }) => {
        expect(submission.blankAnswers).toEqual([
          { blankKey: '1', answer: 'are' },
          { blankKey: '2', answer: 'am' },
        ]);
        return of({
          exerciseId: fillExercise.id,
          score: 2,
          maxScore: 2,
          percentage: 100,
          explanation: 'Use the verb form for the subject.',
          results: [
            { itemKey: '1', itemType: 'blank' as const, submittedAnswer: 'are', isCorrect: true, correctAnswer: 'Are', explanation: 'Use are with you.', points: 1 },
            { itemKey: '2', itemType: 'blank' as const, submittedAnswer: 'am', isCorrect: true, correctAnswer: 'am', explanation: 'Use am with I.', points: 1 },
          ],
        });
      },
    };

    await TestBed.configureTestingModule({
      imports: [PracticePage],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ slug: 'present-simple-to-be' })) },
        },
        { provide: GrammarApiService, useValue: api },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PracticePage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelectorAll('.exercise-text select, .exercise-text input').length).toBe(2);
    expect(page.querySelector('.exercise-text')?.textContent).toContain('you a teacher?');
    const select = page.querySelector('#blank-1') as HTMLSelectElement;
    select.value = 'are';
    select.dispatchEvent(new Event('change'));
    const input = page.querySelector('#blank-2') as HTMLInputElement;
    input.value = 'am';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.primary-button')?.click();
    fixture.detectChanges();

    expect(page.textContent).toContain('Resultado: 2 de 2 pontos (100%)');
    expect(page.textContent).toContain('Use are with you.');
    expect(page.textContent).toContain('Correta');
  });

  it('keeps typed answers when navigating to another exercise and back', async () => {
    const secondExercise: GrammarExercise = {
      ...fillExercise,
      id: 'exercise-2',
      title: 'Second exercise',
    };
    const list: GrammarExerciseSummary[] = [fillExercise, secondExercise].map((item, index) => ({
      id: item.id,
      title: item.title,
      exerciseType: item.exerciseType,
      displayOrder: index + 1,
      itemCount: item.itemCount,
    }));
    const api = {
      getExercises: () => of(list),
      getExercise: (id: string) => of(id === fillExercise.id ? fillExercise : secondExercise),
      submit: () => of(),
    };
    await TestBed.configureTestingModule({
      imports: [PracticePage],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ slug: 'present-simple-to-be' })) },
        },
        { provide: GrammarApiService, useValue: api },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PracticePage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const select = page.querySelector('#blank-1') as HTMLSelectElement;
    select.value = 'are';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    page.querySelector<HTMLButtonElement>('button:last-child')?.click();
    fixture.detectChanges();
    expect(page.textContent).toContain('Second exercise');
    page.querySelector<HTMLButtonElement>('button:first-of-type')?.click();
    fixture.detectChanges();

    expect(page.querySelector('#blank-1')).not.toBeNull();
    expect((page.querySelector('#blank-1') as HTMLSelectElement).value).toBe('are');
    TestBed.inject(GrammarSessionStore).reset('exercise-1');
  });
});

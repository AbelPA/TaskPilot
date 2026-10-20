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
    {
      key: '1',
      displayOrder: 1,
      inputType: 'select',
      points: 1,
      options: [
        { key: 'am', text: 'am', displayOrder: 1 },
        { key: 'are', text: 'are', displayOrder: 2 },
      ],
    },
    { key: '2', displayOrder: 2, inputType: 'text', points: 1, options: [] },
  ],
  questions: [],
};

const choiceExercise: GrammarExercise = {
  id: 'exercise-2',
  title: 'Choose the correct form',
  instruction: 'Select the correct answer.',
  exerciseType: 'multiple_choice',
  contentTemplate: 'She ___ a teacher.',
  itemCount: 1,
  blanks: [],
  questions: [
    {
      key: '1',
      prompt: 'Which verb completes the sentence?',
      displayOrder: 1,
      points: 1,
      options: [
        { key: 'A', text: 'are', displayOrder: 1 },
        { key: 'B', text: 'is', displayOrder: 2 },
      ],
    },
  ],
};

describe('PracticePage', () => {
  function exerciseList(): GrammarExerciseSummary[] {
    return [fillExercise, choiceExercise].map((exercise, index) => ({
      id: exercise.id,
      title: exercise.title,
      exerciseType: exercise.exerciseType,
      displayOrder: index + 1,
      itemCount: exercise.itemCount,
    }));
  }

  function result(exerciseId: string, itemKey: string, itemType: 'blank' | 'question') {
    return {
      exerciseId,
      score: 1,
      maxScore: 1,
      percentage: 100,
      explanation: `Feedback for ${exerciseId}.`,
      results: [
        {
          itemKey,
          itemType,
          submittedAnswer: itemType === 'blank' ? 'are' : 'B',
          isCorrect: true,
          correctAnswer: itemType === 'blank' ? 'Are' : 'is',
          explanation: `Explanation for ${exerciseId}.`,
          points: 1,
        },
      ],
    };
  }

  async function renderPage(api: object) {
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
    return fixture;
  }

  it('renders all exercises and shows feedback for each after submitting', async () => {
    const submissions: string[] = [];
    const api = {
      getExercises: () => of(exerciseList()),
      getExercise: (id: string) => of(id === fillExercise.id ? fillExercise : choiceExercise),
      submit: (id: string, submission: {
        blankAnswers: Array<{ blankKey: string; answer: string }>;
        questionAnswers: Array<{ questionKey: string; optionKey: string }>;
      }) => {
        submissions.push(id);
        if (id === fillExercise.id) {
          expect(submission.blankAnswers).toEqual([
            { blankKey: '1', answer: 'are' },
            { blankKey: '2', answer: 'am' },
          ]);
          return of(result(id, '1', 'blank'));
        }
        expect(submission.questionAnswers).toEqual([{ questionKey: '1', optionKey: 'B' }]);
        return of(result(id, '1', 'question'));
      },
    };
    const fixture = await renderPage(api);
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelectorAll('.exercise-card').length).toBe(2);
    expect(page.querySelectorAll('.exercise-text select, .exercise-text input').length).toBe(2);
    expect(page.querySelector('#blank-exercise-1-1')).not.toBeNull();
    expect(page.querySelector('#question-exercise-2-1')).not.toBeNull();

    const blankSelect = page.querySelector('#blank-exercise-1-1') as HTMLSelectElement;
    blankSelect.value = 'are';
    blankSelect.dispatchEvent(new Event('change'));
    const blankInput = page.querySelector('#blank-exercise-1-2') as HTMLInputElement;
    blankInput.value = 'am';
    blankInput.dispatchEvent(new Event('input'));
    const questionSelect = page.querySelector('#question-exercise-2-1') as HTMLSelectElement;
    questionSelect.value = 'B';
    questionSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    page.querySelector<HTMLButtonElement>('.primary-button')?.click();
    fixture.detectChanges();

    expect(submissions).toEqual(['exercise-1', 'exercise-2']);
    expect(page.textContent).toContain('Feedback for exercise-1.');
    expect(page.textContent).toContain('Feedback for exercise-2.');
    expect(page.textContent).toContain('Explanation for exercise-1.');
    expect(page.textContent).toContain('Explanation for exercise-2.');
    expect(page.querySelectorAll('.result-panel').length).toBe(2);
  });

  it('keeps answers independent per exercise and retries the full list', async () => {
    const api = {
      getExercises: () => of(exerciseList()),
      getExercise: (id: string) => of(id === fillExercise.id ? fillExercise : choiceExercise),
      submit: (id: string) => of(result(id, '1', id === fillExercise.id ? 'blank' : 'question')),
    };
    const fixture = await renderPage(api);
    const page = fixture.nativeElement as HTMLElement;
    const blankSelect = page.querySelector('#blank-exercise-1-1') as HTMLSelectElement;
    blankSelect.value = 'are';
    blankSelect.dispatchEvent(new Event('change'));
    const questionSelect = page.querySelector('#question-exercise-2-1') as HTMLSelectElement;
    questionSelect.value = 'B';
    questionSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(blankSelect.value).toBe('are');
    expect(questionSelect.value).toBe('B');

    page.querySelector<HTMLButtonElement>('.primary-button')?.click();
    fixture.detectChanges();
    page.querySelector<HTMLButtonElement>('.secondary-button')?.click();
    fixture.detectChanges();

    expect(page.querySelectorAll('.result-panel').length).toBe(0);
    expect((page.querySelector('#blank-exercise-1-1') as HTMLSelectElement).value).toBe('');
    expect((page.querySelector('#question-exercise-2-1') as HTMLSelectElement).value).toBe('');
    TestBed.inject(GrammarSessionStore).reset(fillExercise.id);
    TestBed.inject(GrammarSessionStore).reset(choiceExercise.id);
  });
});

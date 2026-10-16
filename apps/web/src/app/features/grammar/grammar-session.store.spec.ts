import { TestBed } from '@angular/core/testing';
import { GrammarSessionStore } from './grammar-session.store';

describe('GrammarSessionStore', () => {
  let store: GrammarSessionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    store = TestBed.inject(GrammarSessionStore);
  });

  it('retains answers independently for each exercise and clears only the retried exercise', () => {
    store.setAnswer('exercise-1', '1', 'are');
    store.setQuestionAnswer('exercise-2', '1', 'B');

    expect(store.get('exercise-1').submission.blankAnswers).toEqual([
      { blankKey: '1', answer: 'are' },
    ]);
    expect(store.get('exercise-2').submission.questionAnswers).toEqual([
      { questionKey: '1', optionKey: 'B' },
    ]);

    store.reset('exercise-1');
    expect(store.get('exercise-1').submission.blankAnswers).toEqual([]);
    expect(store.get('exercise-2').submission.questionAnswers).toEqual([
      { questionKey: '1', optionKey: 'B' },
    ]);
  });
});

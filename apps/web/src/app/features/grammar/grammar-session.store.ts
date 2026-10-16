import { Injectable, signal } from '@angular/core';
import type { GrammarSubmission } from './grammar-api.service';

export interface GrammarSessionItemResult {
  itemKey: string;
  itemType: 'blank' | 'question';
  submittedAnswer: string;
  isCorrect: boolean;
  correctAnswer: string;
  explanation: string;
  points: number;
}

export interface GrammarSessionResult {
  exerciseId: string;
  score: number;
  maxScore: number;
  percentage: number;
  explanation: string;
  results: GrammarSessionItemResult[];
}

export interface GrammarExerciseSession {
  submission: GrammarSubmission;
  result: GrammarSessionResult | null;
}

@Injectable({ providedIn: 'root' })
export class GrammarSessionStore {
  private readonly sessions = signal<ReadonlyMap<string, GrammarExerciseSession>>(new Map());

  get(exerciseId: string): GrammarExerciseSession {
    return this.sessions().get(exerciseId) ?? {
      submission: { blankAnswers: [], questionAnswers: [] },
      result: null,
    };
  }

  setAnswer(exerciseId: string, blankKey: string, answer: string): void {
    const current = this.get(exerciseId);
    const blanks = new Map(current.submission.blankAnswers.map(item => [item.blankKey, item.answer]));
    blanks.set(blankKey, answer);
    this.update(exerciseId, {
      submission: {
        ...current.submission,
        blankAnswers: [...blanks].map(([key, value]) => ({ blankKey: key, answer: value })),
      },
      result: null,
    });
  }

  setQuestionAnswer(exerciseId: string, questionKey: string, optionKey: string): void {
    const current = this.get(exerciseId);
    const questions = new Map(
      current.submission.questionAnswers.map(item => [item.questionKey, item.optionKey]),
    );
    questions.set(questionKey, optionKey);
    this.update(exerciseId, {
      submission: {
        ...current.submission,
        questionAnswers: [...questions].map(([key, value]) => ({
          questionKey: key,
          optionKey: value,
        })),
      },
      result: null,
    });
  }

  setResult(exerciseId: string, result: GrammarSessionResult): void {
    this.update(exerciseId, { ...this.get(exerciseId), result });
  }

  reset(exerciseId: string): void {
    const next = new Map(this.sessions());
    next.delete(exerciseId);
    this.sessions.set(next);
  }

  private update(exerciseId: string, session: GrammarExerciseSession): void {
    const next = new Map(this.sessions());
    next.set(exerciseId, session);
    this.sessions.set(next);
  }
}

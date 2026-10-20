import { Injectable, signal } from '@angular/core';
import type { GrammarReviewSession, GrammarSubmission } from './grammar-api.service';

export interface GrammarSessionItemResult {
  itemKey: string;
  itemType: 'blank' | 'question';
  submittedAnswer: string;
  isCorrect: boolean;
  correctAnswer: string;
  explanation?: string;
  points?: number;
}

export interface GrammarSessionResult {
  submissionId: string;
  attemptNumber: number;
  exerciseId: string;
  score: number;
  maxScore: number;
  percentage: number;
  explanation?: string;
  results: GrammarSessionItemResult[];
}

export type GrammarSubmissionDraft = Omit<GrammarSubmission, 'submissionId'> & {
  submissionId?: string;
};

export interface GrammarExerciseSession {
  submission: GrammarSubmissionDraft;
  result: GrammarSessionResult | null;
}

export interface GrammarReviewSessionState {
  session: GrammarReviewSession;
}

@Injectable({ providedIn: 'root' })
export class GrammarSessionStore {
  private readonly sessions = signal<ReadonlyMap<string, GrammarExerciseSession>>(new Map());
  private readonly reviewSession = signal<GrammarReviewSessionState | null>(null);

  get(exerciseId: string): GrammarExerciseSession {
    return (
      this.sessions().get(exerciseId) ?? {
        submission: { blankAnswers: [], questionAnswers: [] },
        result: null,
      }
    );
  }

  submissionFor(exerciseId: string): GrammarSubmission {
    const current = this.get(exerciseId);
    if (current.submission.submissionId) {
      return { ...current.submission, submissionId: current.submission.submissionId };
    }
    const submission: GrammarSubmission = {
      ...current.submission,
      submissionId: crypto.randomUUID(),
    };
    this.update(exerciseId, { ...current, submission });
    return submission;
  }

  setSubmissionContext(exerciseId: string, topicId: string, reviewSessionId: string | null): void {
    const current = this.get(exerciseId);
    if (
      current.submission.topicId === topicId &&
      current.submission.reviewSessionId === reviewSessionId
    )
      return;
    this.update(exerciseId, {
      submission: {
        ...current.submission,
        submissionId: crypto.randomUUID(),
        topicId,
        reviewSessionId,
        blankAnswers: [],
        questionAnswers: [],
      },
      result: null,
    });
  }

  setReviewSession(session: GrammarReviewSessionState): void {
    this.reviewSession.set(session);
  }

  getReviewSession(): GrammarReviewSessionState | null {
    return this.reviewSession();
  }

  resetReviewSession(): void {
    this.reviewSession.set(null);
  }

  setAnswer(exerciseId: string, blankKey: string, answer: string): void {
    const current = this.get(exerciseId);
    const blanks = new Map(
      current.submission.blankAnswers.map((item) => [item.blankKey, item.answer]),
    );
    blanks.set(blankKey, answer);
    this.update(exerciseId, {
      submission: {
        ...current.submission,
        submissionId: current.submission.submissionId || crypto.randomUUID(),
        blankAnswers: [...blanks].map(([key, value]) => ({ blankKey: key, answer: value })),
      },
      result: null,
    });
  }

  setQuestionAnswer(exerciseId: string, questionKey: string, optionKey: string): void {
    const current = this.get(exerciseId);
    const questions = new Map(
      current.submission.questionAnswers.map((item) => [item.questionKey, item.optionKey]),
    );
    questions.set(questionKey, optionKey);
    this.update(exerciseId, {
      submission: {
        ...current.submission,
        submissionId: current.submission.submissionId || crypto.randomUUID(),
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

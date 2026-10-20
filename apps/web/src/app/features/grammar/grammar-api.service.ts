import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import type { GrammarSessionResult } from './grammar-session.store';

export interface GrammarTopicSummary {
  id: string;
  slug: string;
  title: string;
  description: string;
  level: string;
  displayOrder: number;
  exerciseCount: number;
}

export interface GrammarTopic extends GrammarTopicSummary {
  previousTopicSlug: string | null;
  nextTopicSlug: string | null;
  theorySections: GrammarTheorySection[];
}

export interface GrammarTheorySection {
  title: string;
  displayOrder: number;
  blocks: GrammarTheoryBlock[];
}

export type GrammarTheoryBlock =
  | { type: 'paragraph' | 'note'; text: string }
  | { type: 'example'; text: string; translation?: string | null }
  | { type: 'list'; ordered?: boolean | null; items: string[] }
  | { type: 'table'; headers: string[]; rows: string[][] };

export interface GrammarExerciseSummary {
  id: string;
  title: string;
  exerciseType: 'fill_blank' | 'multiple_choice';
  displayOrder: number;
  itemCount: number;
}

export interface GrammarOption {
  key: string;
  text: string;
  displayOrder: number;
}

export interface GrammarBlank {
  key: string;
  displayOrder: number;
  inputType: 'text' | 'select';
  points: number;
  options: GrammarOption[];
}

export interface GrammarQuestion {
  key: string;
  prompt: string;
  displayOrder: number;
  points: number;
  options: GrammarOption[];
}

export interface GrammarExercise {
  id: string;
  title: string;
  instruction: string;
  exerciseType: 'fill_blank' | 'multiple_choice';
  contentTemplate: string;
  itemCount: number;
  blanks: GrammarBlank[];
  questions: GrammarQuestion[];
}

export interface GrammarSubmission {
  submissionId: string;
  topicId?: string | null;
  reviewSessionId?: string | null;
  blankAnswers: Array<{ blankKey: string; answer: string }>;
  questionAnswers: Array<{ questionKey: string; optionKey: string }>;
}

export interface GrammarTopicStatistics {
  topicId: string;
  topicSlug: string;
  topicTitle: string;
  totalAttempts: number;
  correctAnswers: number;
  incorrectAnswers: number;
  accuracyPercentage: number | null;
  errorPercentage: number | null;
  lastAttemptAt: string | null;
  recentAttemptCount: number;
  recentCorrectAnswers: number;
  recentIncorrectAnswers: number;
  assessed: boolean;
  insufficientEvidence: boolean;
}

export type ReviewSelectionCategory = 'priority' | 'maintenance' | 'exploration';

export interface GrammarReviewExercise {
  exerciseId: string;
  topicId: string;
  title?: string;
  topicTitle?: string;
  selectionCategory: ReviewSelectionCategory;
  priorityScore: number | null;
  reason: string;
  displayOrder: number;
}

export interface GrammarReviewSession {
  sessionId: string;
  standardExerciseCount: 10;
  exerciseCount: number;
  isShorterThanStandard: boolean;
  exercises: GrammarReviewExercise[];
}

export interface GrammarReviewTopicResult {
  topicId: string;
  topicTitle: string;
  correctAnswers: number;
  incorrectAnswers: number;
  needsAttention: boolean;
}

export interface GrammarReviewSummary {
  sessionId: string;
  exercisesAnswered: number;
  correctAnswers: number;
  incorrectAnswers: number;
  accuracyPercentage: number | null;
  topicResults: GrammarReviewTopicResult[];
  priorComparisonAvailable?: boolean;
  priorAccuracyPercentage?: number | null;
}

@Injectable({ providedIn: 'root' })
export class GrammarApiService {
  private readonly http = inject(HttpClient);

  getTopics(): Observable<GrammarTopicSummary[]> {
    return this.http.get<GrammarTopicSummary[]>('/api/grammar/topics');
  }

  getTopic(slug: string): Observable<GrammarTopic> {
    return this.http.get<GrammarTopic>(`/api/grammar/topics/${encodeURIComponent(slug)}`);
  }

  getExercises(slug: string): Observable<GrammarExerciseSummary[]> {
    return this.http.get<GrammarExerciseSummary[]>(
      `/api/grammar/topics/${encodeURIComponent(slug)}/exercises`,
    );
  }

  getExercise(id: string): Observable<GrammarExercise> {
    return this.http.get<GrammarExercise>(`/api/grammar/exercises/${encodeURIComponent(id)}`);
  }

  submit(exerciseId: string, submission: GrammarSubmission): Observable<GrammarSessionResult> {
    return this.http.post<GrammarSessionResult>(
      `/api/grammar/exercises/${encodeURIComponent(exerciseId)}/submit`,
      submission,
    );
  }

  getTopicStatistics(): Observable<GrammarTopicStatistics[]> {
    return this.http.get<GrammarTopicStatistics[]>('/api/grammar/statistics/topics');
  }

  createReviewSession(): Observable<GrammarReviewSession> {
    return this.http.post<GrammarReviewSession>('/api/grammar/review/sessions', null);
  }

  getReviewSummary(sessionId: string): Observable<GrammarReviewSummary> {
    return this.http.get<GrammarReviewSummary>(
      `/api/grammar/review/sessions/${encodeURIComponent(sessionId)}/summary`,
    );
  }
}

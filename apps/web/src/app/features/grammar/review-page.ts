import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of, tap } from 'rxjs';
import {
  GrammarApiService,
  GrammarExercise,
  GrammarOption,
  GrammarReviewExercise,
  GrammarReviewSession,
} from './grammar-api.service';
import {
  GrammarSessionItemResult,
  GrammarSessionResult,
  GrammarSessionStore,
} from './grammar-session.store';

interface ReviewExerciseView {
  metadata: GrammarReviewExercise;
  exercise: GrammarExercise;
}

type PracticeSegment =
  { kind: 'text'; value: string } | { kind: 'blank'; blank: GrammarExercise['blanks'][number] };

@Component({
  selector: 'app-grammar-review-page',
  imports: [RouterLink],
  templateUrl: './review-page.html',
  styleUrl: './review-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewPage implements OnInit {
  private readonly api = inject(GrammarApiService);
  private readonly sessions = inject(GrammarSessionStore);
  protected readonly session = signal<GrammarReviewSession | null>(null);
  protected readonly exercises = signal<ReviewExerciseView[]>([]);
  protected readonly summary = signal<import('./grammar-api.service').GrammarReviewSummary | null>(
    null,
  );
  protected readonly loading = signal(true);
  protected readonly summaryLoading = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly summaryError = signal('');
  protected readonly submittingIds = signal<ReadonlySet<string>>(new Set());

  ngOnInit(): void {
    const savedSession = this.sessions.getReviewSession();
    if (savedSession) this.loadExercises(savedSession.session);
    else this.startReview();
  }

  protected startReview(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.summaryError.set('');
    this.summary.set(null);
    this.session.set(null);
    this.exercises.set([]);
    this.sessions.resetReviewSession();
    this.api.createReviewSession().subscribe({
      next: (session) => this.loadExercises(session),
      error: () => {
        this.errorMessage.set('Não foi possível iniciar uma revisão. Tente novamente.');
        this.loading.set(false);
      },
    });
  }

  protected segments(exercise: GrammarExercise): PracticeSegment[] {
    if (exercise.exerciseType !== 'fill_blank') return [];
    const blanks = new Map(exercise.blanks.map((blank) => [blank.key, blank]));
    const segments: PracticeSegment[] = [];
    const marker = /\{\{blank:([^{}]+)\}\}/g;
    let cursor = 0;
    for (const match of exercise.contentTemplate.matchAll(marker)) {
      const index = match.index;
      if (index > cursor)
        segments.push({ kind: 'text', value: exercise.contentTemplate.slice(cursor, index) });
      const blank = blanks.get(match[1]);
      segments.push(blank ? { kind: 'blank', blank } : { kind: 'text', value: match[0] });
      cursor = index + match[0].length;
    }
    if (cursor < exercise.contentTemplate.length)
      segments.push({ kind: 'text', value: exercise.contentTemplate.slice(cursor) });
    return segments;
  }

  protected answerForBlank(exerciseId: string, key: string): string {
    return (
      this.sessions
        .get(exerciseId)
        .submission.blankAnswers.find((answer) => answer.blankKey === key)?.answer ?? ''
    );
  }

  protected answerForQuestion(exerciseId: string, key: string): string {
    return (
      this.sessions
        .get(exerciseId)
        .submission.questionAnswers.find((answer) => answer.questionKey === key)?.optionKey ?? ''
    );
  }

  protected resultFor(
    exerciseId: string,
    key: string,
    type: 'blank' | 'question',
  ): GrammarSessionItemResult | undefined {
    return this.sessions
      .get(exerciseId)
      .result?.results.find((result) => result.itemKey === key && result.itemType === type);
  }

  protected result(exerciseId: string): GrammarSessionResult | null {
    return this.sessions.get(exerciseId).result;
  }

  protected setBlankAnswer(exerciseId: string, key: string, event: Event): void {
    const target = event.target;
    if (target instanceof HTMLInputElement || target instanceof HTMLSelectElement) {
      this.sessions.setAnswer(exerciseId, key, target.value);
    }
  }

  protected setQuestionAnswer(exerciseId: string, key: string, event: Event): void {
    if (event.target instanceof HTMLSelectElement)
      this.sessions.setQuestionAnswer(exerciseId, key, event.target.value);
  }

  protected submitExercise(view: ReviewExerciseView): void {
    const exerciseId = view.exercise.id;
    if (this.submittingIds().has(exerciseId) || this.sessions.get(exerciseId).result) return;
    this.submittingIds.update((current) => new Set(current).add(exerciseId));
    this.errorMessage.set('');
    this.api
      .submit(exerciseId, this.sessions.submissionFor(exerciseId))
      .pipe(
        tap((result) => this.sessions.setResult(exerciseId, result)),
        catchError(() => {
          this.errorMessage.set(
            'Não foi possível verificar esta resposta. Ela foi mantida para uma nova tentativa.',
          );
          return of(null);
        }),
      )
      .subscribe(() => {
        this.submittingIds.update((current) => {
          const next = new Set(current);
          next.delete(exerciseId);
          return next;
        });
      });
  }

  protected loadSummary(): void {
    const sessionId = this.session()?.sessionId;
    if (!sessionId || this.summaryLoading()) return;
    this.summaryLoading.set(true);
    this.summaryError.set('');
    this.api.getReviewSummary(sessionId).subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.summaryLoading.set(false);
      },
      error: () => {
        this.summaryError.set('Não foi possível carregar o resumo desta revisão. Tente novamente.');
        this.summaryLoading.set(false);
      },
    });
  }

  protected optionLabel(option: GrammarOption): string {
    return `${option.key}) ${option.text}`;
  }
  protected trackExercise(_: number, view: ReviewExerciseView): string {
    return view.exercise.id;
  }

  private loadExercises(session: GrammarReviewSession): void {
    this.session.set(session);
    this.sessions.setReviewSession({ session });
    if (session.exercises.length === 0) {
      this.exercises.set([]);
      this.loading.set(false);
      return;
    }
    forkJoin(
      session.exercises.map((metadata) =>
        this.api
          .getExercise(metadata.exerciseId)
          .pipe(
            tap(() =>
              this.sessions.setSubmissionContext(
                metadata.exerciseId,
                metadata.topicId,
                session.sessionId,
              ),
            ),
          ),
      ),
    ).subscribe({
      next: (loaded) => {
        this.exercises.set(
          session.exercises.map((metadata, index) => ({ metadata, exercise: loaded[index] })),
        );
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set(
          'Não foi possível carregar os exercícios da revisão. Tente novamente.',
        );
        this.loading.set(false);
      },
    });
  }
}

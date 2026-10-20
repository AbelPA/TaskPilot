import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, distinctUntilChanged, forkJoin, map, of, tap } from 'rxjs';
import {
  GrammarApiService,
  GrammarBlank,
  GrammarExercise,
  GrammarOption,
  GrammarQuestion,
} from './grammar-api.service';
import {
  GrammarSessionItemResult,
  GrammarSessionResult,
  GrammarSessionStore,
} from './grammar-session.store';

type PracticeSegment =
  | { kind: 'text'; value: string }
  | { kind: 'blank'; blank: GrammarBlank };

@Component({
  selector: 'app-grammar-practice-page',
  imports: [RouterLink],
  templateUrl: './practice-page.html',
  styleUrl: './practice-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PracticePage implements OnInit {
  private readonly api = inject(GrammarApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sessionStore = inject(GrammarSessionStore);

  protected readonly topicSlug = signal('');
  protected readonly exercises = signal<GrammarExercise[]>([]);
  protected readonly loading = signal(true);
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal('');

  ngOnInit(): void {
    this.route.paramMap
      .pipe(
        map(params => params.get('slug') ?? ''),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(slug => {
        this.topicSlug.set(slug);
        this.loadExercises(slug);
      });
  }

  protected segments(exercise: GrammarExercise): PracticeSegment[] {
    if (exercise.exerciseType !== 'fill_blank') {
      return [];
    }

    const blanks = new Map(exercise.blanks.map(blank => [blank.key, blank]));
    const segments: PracticeSegment[] = [];
    const marker = /\{\{blank:([^{}]+)\}\}/g;
    let cursor = 0;
    for (const match of exercise.contentTemplate.matchAll(marker)) {
      const index = match.index;
      if (index > cursor) {
        segments.push({ kind: 'text', value: exercise.contentTemplate.slice(cursor, index) });
      }
      const blank = blanks.get(match[1]);
      if (blank) {
        segments.push({ kind: 'blank', blank });
      } else {
        segments.push({ kind: 'text', value: match[0] });
      }
      cursor = index + match[0].length;
    }
    if (cursor < exercise.contentTemplate.length) {
      segments.push({ kind: 'text', value: exercise.contentTemplate.slice(cursor) });
    }
    return segments;
  }

  protected answerForBlank(exerciseId: string, key: string): string {
    return this.sessionStore.get(exerciseId).submission.blankAnswers.find(
      answer => answer.blankKey === key,
    )?.answer ?? '';
  }

  protected answerForQuestion(exerciseId: string, key: string): string {
    return this.sessionStore.get(exerciseId).submission.questionAnswers.find(
      answer => answer.questionKey === key,
    )?.optionKey ?? '';
  }

  protected resultFor(
    exerciseId: string,
    key: string,
    type: 'blank' | 'question',
  ): GrammarSessionItemResult | undefined {
    return this.sessionStore.get(exerciseId).result?.results.find(
      result => result.itemKey === key && result.itemType === type,
    );
  }

  protected sessionResult(exerciseId: string): GrammarSessionResult | null {
    return this.sessionStore.get(exerciseId).result;
  }

  protected setBlankAnswer(exerciseId: string, key: string, event: Event): void {
    const input = event.target;
    if (input instanceof HTMLInputElement || input instanceof HTMLSelectElement) {
      this.sessionStore.setAnswer(exerciseId, key, input.value);
    }
  }

  protected setQuestionAnswer(exerciseId: string, key: string, event: Event): void {
    const input = event.target;
    if (input instanceof HTMLSelectElement) {
      this.sessionStore.setQuestionAnswer(exerciseId, key, input.value);
    }
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }

    const pendingExercises = this.exercises().filter(
      exercise => !this.sessionStore.get(exercise.id).result,
    );
    if (pendingExercises.length === 0) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    const failedExerciseIds: string[] = [];
    forkJoin(
      pendingExercises.map(exercise =>
        this.api.submit(exercise.id, this.sessionStore.get(exercise.id).submission).pipe(
          tap(result => this.sessionStore.setResult(exercise.id, result)),
          catchError(() => {
            failedExerciseIds.push(exercise.id);
            return of(null);
          }),
        ),
      ),
    ).subscribe({
      next: () => {
        if (failedExerciseIds.length > 0) {
          this.errorMessage.set(
            failedExerciseIds.length === 1
              ? 'Não foi possível verificar um exercício. As respostas foram mantidas; tente novamente.'
              : `Não foi possível verificar ${failedExerciseIds.length} exercícios. As respostas foram mantidas; tente novamente.`,
          );
        }
        this.submitting.set(false);
      },
    });
  }

  protected retry(): void {
    for (const exercise of this.exercises()) {
      this.sessionStore.reset(exercise.id);
    }
    this.errorMessage.set('');
  }

  protected allExercisesChecked(): boolean {
    return this.exercises().every(exercise => !!this.sessionStore.get(exercise.id).result);
  }

  protected optionLabel(option: GrammarOption): string {
    return `${option.key}) ${option.text}`;
  }

  protected trackQuestion(_: number, question: GrammarQuestion): string {
    return question.key;
  }

  protected trackExercise(_: number, exercise: GrammarExercise): string {
    return exercise.id;
  }

  private loadExercises(slug: string): void {
    this.loading.set(true);
    this.exercises.set([]);
    this.errorMessage.set('');
    if (!slug) {
      this.errorMessage.set('O tópico solicitado não foi encontrado.');
      this.loading.set(false);
      return;
    }

    this.api.getExercises(slug).subscribe({
      next: exercises => {
        if (exercises.length === 0) {
          this.errorMessage.set('Ainda não há exercícios disponíveis para este tópico.');
          this.loading.set(false);
          return;
        }
        forkJoin(exercises.map(exercise => this.api.getExercise(exercise.id))).subscribe({
          next: loadedExercises => {
            this.exercises.set(loadedExercises);
            this.loading.set(false);
          },
          error: () => {
            this.errorMessage.set('Não foi possível carregar os exercícios. Tente novamente.');
            this.loading.set(false);
          },
        });
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar os exercícios. Tente novamente.');
        this.loading.set(false);
      },
    });
  }
}

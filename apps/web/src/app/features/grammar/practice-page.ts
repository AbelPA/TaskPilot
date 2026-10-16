import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, map } from 'rxjs';
import {
  GrammarApiService,
  GrammarBlank,
  GrammarExercise,
  GrammarExerciseSummary,
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
  protected readonly exercises = signal<GrammarExerciseSummary[]>([]);
  protected readonly exercise = signal<GrammarExercise | null>(null);
  protected readonly exerciseIndex = signal(0);
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

  protected segments(): PracticeSegment[] {
    const current = this.exercise();
    if (!current || current.exerciseType !== 'fill_blank') {
      return [];
    }

    const blanks = new Map(current.blanks.map(blank => [blank.key, blank]));
    const segments: PracticeSegment[] = [];
    const marker = /\{\{blank:([^{}]+)\}\}/g;
    let cursor = 0;
    for (const match of current.contentTemplate.matchAll(marker)) {
      const index = match.index;
      if (index > cursor) {
        segments.push({ kind: 'text', value: current.contentTemplate.slice(cursor, index) });
      }
      const blank = blanks.get(match[1]);
      if (blank) {
        segments.push({ kind: 'blank', blank });
      } else {
        segments.push({ kind: 'text', value: match[0] });
      }
      cursor = index + match[0].length;
    }
    if (cursor < current.contentTemplate.length) {
      segments.push({ kind: 'text', value: current.contentTemplate.slice(cursor) });
    }
    return segments;
  }

  protected answerForBlank(key: string): string {
    const id = this.exercise()?.id;
    return id
      ? this.sessionStore.get(id).submission.blankAnswers.find(answer => answer.blankKey === key)?.answer ?? ''
      : '';
  }

  protected answerForQuestion(key: string): string {
    const id = this.exercise()?.id;
    return id
      ? this.sessionStore.get(id).submission.questionAnswers.find(answer => answer.questionKey === key)?.optionKey ?? ''
      : '';
  }

  protected resultFor(key: string, type: 'blank' | 'question'): GrammarSessionItemResult | undefined {
    const id = this.exercise()?.id;
    return id
      ? this.sessionStore.get(id).result?.results.find(result => result.itemKey === key && result.itemType === type)
      : undefined;
  }

  protected sessionResult(exerciseId: string): GrammarSessionResult | null {
    return this.sessionStore.get(exerciseId).result;
  }

  protected setBlankAnswer(key: string, event: Event): void {
    const input = event.target;
    const id = this.exercise()?.id;
    if (id && (input instanceof HTMLInputElement || input instanceof HTMLSelectElement)) {
      this.sessionStore.setAnswer(id, key, input.value);
    }
  }

  protected setQuestionAnswer(key: string, event: Event): void {
    const input = event.target;
    const id = this.exercise()?.id;
    if (id && input instanceof HTMLSelectElement) {
      this.sessionStore.setQuestionAnswer(id, key, input.value);
    }
  }

  protected submit(): void {
    const current = this.exercise();
    if (!current || this.submitting() || this.sessionStore.get(current.id).result) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    this.api.submit(current.id, this.sessionStore.get(current.id).submission).subscribe({
      next: result => {
        this.sessionStore.setResult(current.id, result);
        this.submitting.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(
          error instanceof HttpErrorResponse && error.status === 0
            ? 'Não foi possível conectar ao serviço de exercícios. Suas respostas foram mantidas; tente novamente.'
            : 'Não foi possível verificar as respostas. Elas foram mantidas; tente novamente.',
        );
        this.submitting.set(false);
      },
    });
  }

  protected retry(): void {
    const id = this.exercise()?.id;
    if (id) {
      this.sessionStore.reset(id);
      this.errorMessage.set('');
    }
  }

  protected goToExercise(index: number): void {
    if (index < 0 || index >= this.exercises().length || this.loading()) {
      return;
    }
    this.exerciseIndex.set(index);
    this.loadExercise(this.exercises()[index].id);
  }

  protected optionLabel(option: GrammarOption): string {
    return `${option.key}) ${option.text}`;
  }

  protected trackQuestion(_: number, question: GrammarQuestion): string {
    return question.key;
  }

  private loadExercises(slug: string): void {
    this.loading.set(true);
    this.exercise.set(null);
    this.errorMessage.set('');
    if (!slug) {
      this.errorMessage.set('O tópico solicitado não foi encontrado.');
      this.loading.set(false);
      return;
    }

    this.api.getExercises(slug).subscribe({
      next: exercises => {
        this.exercises.set(exercises);
        if (exercises.length === 0) {
          this.errorMessage.set('Ainda não há exercícios disponíveis para este tópico.');
          this.loading.set(false);
          return;
        }
        this.exerciseIndex.set(0);
        this.loadExercise(exercises[0].id);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar os exercícios. Tente novamente.');
        this.loading.set(false);
      },
    });
  }

  private loadExercise(id: string): void {
    this.loading.set(true);
    this.exercise.set(null);
    this.errorMessage.set('');
    this.api.getExercise(id).subscribe({
      next: exercise => {
        this.exercise.set(exercise);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar este exercício. Tente novamente.');
        this.loading.set(false);
      },
    });
  }
}

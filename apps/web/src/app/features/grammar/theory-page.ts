import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, map, of, switchMap } from 'rxjs';
import { GrammarApiService, GrammarTheoryBlock, GrammarTopic } from './grammar-api.service';

@Component({
  selector: 'app-grammar-theory-page',
  imports: [RouterLink],
  templateUrl: './theory-page.html',
  styleUrl: './theory-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TheoryPage implements OnInit {
  private readonly api = inject(GrammarApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly topic = signal<GrammarTopic | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal('');

  ngOnInit(): void {
    this.route.paramMap
      .pipe(
        map(params => params.get('slug')),
        distinctUntilChanged(),
        switchMap(slug => slug ? this.api.getTopic(slug) : of(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: topic => {
          this.topic.set(topic);
          this.loading.set(false);
          this.errorMessage.set(topic ? '' : 'O tópico solicitado não foi encontrado.');
        },
        error: () => {
          this.topic.set(null);
          this.loading.set(false);
          this.errorMessage.set('Não foi possível carregar a teoria. Tente novamente.');
        },
      });
  }

  protected isList(block: GrammarTheoryBlock): block is Extract<GrammarTheoryBlock, { type: 'list' }> {
    return block.type === 'list';
  }

  protected isTable(block: GrammarTheoryBlock): block is Extract<GrammarTheoryBlock, { type: 'table' }> {
    return block.type === 'table';
  }

  protected isExample(block: GrammarTheoryBlock): block is Extract<GrammarTheoryBlock, { type: 'example' }> {
    return block.type === 'example';
  }
}

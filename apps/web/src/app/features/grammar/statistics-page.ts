import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GrammarApiService, GrammarTopicStatistics } from './grammar-api.service';

@Component({
  selector: 'app-grammar-statistics-page',
  imports: [RouterLink],
  templateUrl: './statistics-page.html',
  styleUrl: './statistics-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatisticsPage implements OnInit {
  private readonly api = inject(GrammarApiService);
  protected readonly topics = signal<GrammarTopicStatistics[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal('');

  ngOnInit(): void {
    this.loadStatistics();
  }

  protected loadStatistics(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getTopicStatistics().subscribe({
      next: (topics) => {
        this.topics.set(topics);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar as estatísticas. Tente novamente.');
        this.loading.set(false);
      },
    });
  }

  protected formatDate(value: string | null): string {
    if (!value) return 'Sem respostas avaliadas';
    return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'medium', timeStyle: 'short' }).format(
      new Date(value),
    );
  }

  protected trackTopic(_: number, topic: GrammarTopicStatistics): string {
    return topic.topicId;
  }
}

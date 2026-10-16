import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GrammarApiService, GrammarTopicSummary } from './grammar-api.service';

@Component({
  selector: 'app-grammar-topics-page',
  imports: [RouterLink],
  templateUrl: './topics-page.html',
  styleUrl: './topics-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TopicsPage implements OnInit {
  private readonly api = inject(GrammarApiService);
  protected readonly topics = signal<GrammarTopicSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal('');

  ngOnInit(): void {
    this.loadTopics();
  }

  protected loadTopics(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getTopics().subscribe({
      next: topics => {
        this.topics.set(topics);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar os tópicos. Tente novamente.');
        this.loading.set(false);
      },
    });
  }
}

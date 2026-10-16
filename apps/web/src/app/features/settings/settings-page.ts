import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { SettingsApiService, YouTubeApiKeyStatus } from './settings-api.service';

@Component({
  selector: 'app-settings-page',
  imports: [FormsModule],
  templateUrl: './settings-page.html',
  styleUrl: './settings-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(SettingsApiService);
  protected readonly status = signal<YouTubeApiKeyStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly successMessage = signal('');
  protected apiKey = '';

  ngOnInit(): void {
    this.loadStatus();
  }

  protected loadStatus(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getYouTubeApiKeyStatus().subscribe({
      next: status => {
        this.status.set(status);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar as configurações. Tente novamente.');
        this.loading.set(false);
      },
    });
  }

  protected save(): void {
    const value = this.apiKey.trim();
    if (!value || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    this.api.saveYouTubeApiKey(value).subscribe({
      next: status => {
        this.apiKey = '';
        this.status.set(status);
        this.saving.set(false);
        this.successMessage.set('Chave salva com segurança.');
      },
      error: (error: HttpErrorResponse) => {
        this.errorMessage.set(this.readProblem(error, 'Não foi possível salvar a chave.'));
        this.saving.set(false);
      },
    });
  }

  protected remove(): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    this.api.removeYouTubeApiKey().subscribe({
      next: status => {
        this.status.set(status);
        this.saving.set(false);
        this.successMessage.set('Chave removida.');
      },
      error: () => {
        this.errorMessage.set('Não foi possível remover a chave.');
        this.saving.set(false);
      },
    });
  }

  protected describeSource(status: YouTubeApiKeyStatus): string {
    switch (status.source) {
      case 'database':
        return 'Chave salva (criptografada) no banco de dados.';
      case 'environment':
        return 'Usando a chave da variável de ambiente do servidor.';
      default:
        return 'Nenhuma chave configurada. A extração de áudio não funcionará até que ela seja informada.';
    }
  }

  private readProblem(error: HttpErrorResponse, fallback: string): string {
    const detail = (error.error as { detail?: unknown } | null)?.detail;
    return typeof detail === 'string' && detail ? detail : fallback;
  }
}

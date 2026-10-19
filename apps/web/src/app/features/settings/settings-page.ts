import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { SettingsApiService, YouTubeAccountStatus, YouTubeApiKeyStatus } from './settings-api.service';

@Component({
  selector: 'app-settings-page',
  imports: [FormsModule],
  templateUrl: './settings-page.html',
  styleUrl: './settings-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(SettingsApiService);
  protected maskedValue = '';
  protected maskedUsername = '';
  protected maskedPassword = '';
  protected readonly status = signal<YouTubeApiKeyStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly successMessage = signal('');
  protected apiKey = '';
  protected readonly accountStatus = signal<YouTubeAccountStatus | null>(null);
  protected readonly accountSaving = signal(false);
  protected readonly accountError = signal('');
  protected readonly accountSuccess = signal('');
  protected accountUsername = '';
  protected accountPassword = '';

  ngOnInit(): void {
    this.loadStatus();
  }

  protected loadStatus(): void {
    this.api.getYouTubeAccountStatus().subscribe({
      next: status => this.applyAccountStatus(status),
      error: () => this.accountError.set('Não foi possível carregar a conta do YouTube.'),
    });
    this.loading.set(true);
    this.errorMessage.set('');
    this.api.getYouTubeApiKeyStatus().subscribe({
      next: status => {
        this.applyKeyStatus(status);
        this.loading.set(false);
      },
      error: () => {
        this.errorMessage.set('Não foi possível carregar as configurações. Tente novamente.');
        this.loading.set(false);
      },
    });
  }

  protected get keyChanged(): boolean {
    const value = this.apiKey.trim();
    return !!value && value !== this.maskedValue;
  }

  protected get accountChanged(): boolean {
    const username = this.accountUsername.trim();
    return (
      !!username &&
      !!this.accountPassword &&
      username !== this.maskedUsername &&
      this.accountPassword !== this.maskedPassword
    );
  }

  protected save(): void {
    const value = this.apiKey.trim();
    if (!this.keyChanged || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    this.api.saveYouTubeApiKey(value).subscribe({
      next: status => {
        this.applyKeyStatus(status);
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
        this.applyKeyStatus(status);
        this.saving.set(false);
        this.successMessage.set('Chave removida.');
      },
      error: () => {
        this.errorMessage.set('Não foi possível remover a chave.');
        this.saving.set(false);
      },
    });
  }

  protected saveAccount(): void {
    const username = this.accountUsername.trim();
    if (!this.accountChanged || this.accountSaving()) {
      return;
    }

    this.accountSaving.set(true);
    this.accountError.set('');
    this.accountSuccess.set('');
    this.api.saveYouTubeAccount(username, this.accountPassword).subscribe({
      next: status => {
        this.applyAccountStatus(status);
        this.accountSaving.set(false);
        this.accountSuccess.set('Conta salva com segurança.');
      },
      error: (error: HttpErrorResponse) => {
        this.accountError.set(this.readProblem(error, 'Não foi possível salvar a conta.'));
        this.accountSaving.set(false);
      },
    });
  }

  protected removeAccount(): void {
    if (this.accountSaving()) {
      return;
    }

    this.accountSaving.set(true);
    this.accountError.set('');
    this.accountSuccess.set('');
    this.api.removeYouTubeAccount().subscribe({
      next: status => {
        this.applyAccountStatus(status);
        this.accountSaving.set(false);
        this.accountSuccess.set('Conta removida.');
      },
      error: () => {
        this.accountError.set('Não foi possível remover a conta.');
        this.accountSaving.set(false);
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

  private applyKeyStatus(status: YouTubeApiKeyStatus): void {
    this.status.set(status);
    this.maskedValue = status.maskedValue ?? '';
    this.apiKey = this.maskedValue;
  }

  private applyAccountStatus(status: YouTubeAccountStatus): void {
    this.accountStatus.set(status);
    this.maskedUsername = status.maskedUsername ?? '';
    this.maskedPassword = status.maskedPassword ?? '';
    this.accountUsername = this.maskedUsername;
    this.accountPassword = this.maskedPassword;
  }

  private readProblem(error: HttpErrorResponse, fallback: string): string {
    const detail = (error.error as { detail?: unknown } | null)?.detail;
    return typeof detail === 'string' && detail ? detail : fallback;
  }
}

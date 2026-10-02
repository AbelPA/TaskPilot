import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { AudioExtractionApiService, AcceptedAudioExtraction } from './audio-extraction-api.service';
import { AudioExtractionNotificationService } from './notifications/audio-extraction-notification.service';

const TIME_PATTERN = /^[0-9]{2,}:[0-5][0-9]:[0-5][0-9]$/;

function youtubeUrlValidator(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value.trim();
  if (!value) {
    return null;
  }

  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return { youtubeUrl: true };
  }

  const hosts = new Set([
    'youtube.com',
    'www.youtube.com',
    'm.youtube.com',
    'youtu.be',
    'www.youtu.be',
    'youtube-nocookie.com',
    'www.youtube-nocookie.com',
  ]);
  const allowedParameters = new Set(['v', 't', 'start', 'feature', 'si']);
  if (
    url.protocol !== 'https:' ||
    !hosts.has(url.hostname) ||
    url.username ||
    url.password ||
    [...url.searchParams.keys()].some((key) => !allowedParameters.has(key)) ||
    [...allowedParameters].some((key) => url.searchParams.getAll(key).length > 1)
  ) {
    return { youtubeUrl: true };
  }

  const path = url.pathname.split('/').filter(Boolean);
  const id =
    url.hostname.endsWith('youtu.be') && path.length === 1
      ? path[0]
      : path.length === 2 && ['embed', 'shorts', 'live'].includes(path[0])
        ? path[1]
        : url.pathname === '/watch'
          ? url.searchParams.get('v')
          : null;
  return id && /^[A-Za-z0-9_-]{11}$/.test(id) ? null : { youtubeUrl: true };
}

function seconds(value: string): number {
  const [hours, minutes, remainingSeconds] = value.split(':').map(Number);
  return hours * 3600 + minutes * 60 + remainingSeconds;
}

function intervalValidator(control: AbstractControl): ValidationErrors | null {
  const start = control.get('start')?.value as string;
  const end = control.get('end')?.value as string;
  if (!TIME_PATTERN.test(start ?? '') || !TIME_PATTERN.test(end ?? '')) {
    return null;
  }

  const startSeconds = seconds(start);
  const endSeconds = seconds(end);
  if (startSeconds >= endSeconds) {
    return { intervalOrder: true };
  }
  return endSeconds - startSeconds > 1_800 ? { intervalTooLong: true } : null;
}

@Component({
  selector: 'app-audio-extraction-page',
  imports: [ReactiveFormsModule],
  templateUrl: './audio-extraction-page.html',
  styleUrl: './audio-extraction-page.scss',
})
export class AudioExtractionPage {
  private readonly api = inject(AudioExtractionApiService);
  private readonly notifications = inject(AudioExtractionNotificationService);
  protected readonly submitting = signal(false);
  protected readonly accepted = signal<AcceptedAudioExtraction | null>(null);
  protected readonly errorMessage = signal('');

  protected readonly form = new FormGroup(
    {
      url: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, youtubeUrlValidator],
      }),
      start: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(TIME_PATTERN)],
      }),
      end: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(TIME_PATTERN)],
      }),
    },
    { validators: intervalValidator },
  );

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    this.accepted.set(null);
    this.api.submit(this.form.getRawValue()).subscribe({
      next: (response) => {
        this.accepted.set(response);
        this.notifications.trackRequest(response.requestId);
        this.submitting.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(this.getErrorMessage(error));
        this.submitting.set(false);
      },
    });
  }

  private getErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const problem = error.error as { detail?: unknown; title?: unknown } | null;
      if (typeof problem?.detail === 'string' && problem.detail.length > 0) {
        return problem.detail;
      }
      if (error.status === 429) {
        return 'Muitas solicitações. Aguarde um minuto e tente novamente.';
      }
      if (error.status === 503) {
        return 'O serviço está temporariamente indisponível. Tente novamente.';
      }
      if (typeof problem?.title === 'string' && problem.title.length > 0) {
        return problem.title;
      }
    }
    return 'Não foi possível enviar a solicitação. Verifique sua conexão e tente novamente.';
  }
}

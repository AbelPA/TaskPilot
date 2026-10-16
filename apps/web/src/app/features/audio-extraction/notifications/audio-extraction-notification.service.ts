import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { PLATFORM_ID } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AudioExtractionApiService, AudioExtractionStatus } from '../audio-extraction-api.service';
import { RequestCapabilityStore } from './request-capability-store';

const REQUEST_ID_PATTERN = /^[A-Za-z0-9_-]{43}$/;

export interface AudioExtractionEventSource {
  readonly readyState: number;
  onopen: ((event: Event) => unknown) | null;
  onerror: ((event: Event) => unknown) | null;
  addEventListener(type: string, listener: EventListener): void;
  close(): void;
}

export type AudioExtractionNotificationOutcome =
  AudioExtractionStatus | (Omit<AudioExtractionStatus, 'status'> & { status: 'expired' });

export const AUDIO_EXTRACTION_EVENT_SOURCE_FACTORY = new InjectionToken<
  (url: string) => AudioExtractionEventSource
>('AUDIO_EXTRACTION_EVENT_SOURCE_FACTORY', {
  providedIn: 'root',
  factory: () => (url) => new EventSource(url),
});

@Injectable({ providedIn: 'root' })
export class AudioExtractionNotificationService {
  private readonly api = inject(AudioExtractionApiService);
  private readonly createEventSource = inject(AUDIO_EXTRACTION_EVENT_SOURCE_FACTORY);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly capabilityStore = inject(RequestCapabilityStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requestIds = signal<string[]>([]);
  private readonly readIds = signal<string[]>([]);
  private source?: AudioExtractionEventSource;
  private sourceRequestIds: string[] = [];
  private registration?: Promise<void>;
  private recovery?: Promise<void>;
  private recoveryRequested = false;
  private streamVersion = 0;
  private hasStarted = false;

  readonly outcomes = signal<AudioExtractionNotificationOutcome[]>([]);
  readonly connectionState = signal<'disconnected' | 'connecting' | 'connected'>('disconnected');
  readonly recoveryError = signal('');
  readonly unreadCount = computed(
    () => this.outcomes().filter((outcome) => !this.readIds().includes(outcome.requestId)).length,
  );

  constructor() {
    this.destroyRef.onDestroy(() => this.closeStream());
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }
    this.requestIds.set(this.capabilityStore.getRequestIds(() => this.invalidLocalState()));
    this.readIds.set(this.capabilityStore.getReadIds(() => this.invalidLocalState()));
  }

  async connect(): Promise<void> {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }
    if (this.source || this.registration) {
      return this.registration;
    }

    this.hasStarted = true;
    this.requestIds.set(this.capabilityStore.getRequestIds(() => this.invalidLocalState()));
    this.readIds.set(this.capabilityStore.getReadIds(() => this.invalidLocalState()));
    return this.registerStream();
  }

  trackRequest(requestId: string): void {
    if (!REQUEST_ID_PATTERN.test(requestId)) {
      this.recoveryError.set('Não foi possível guardar o identificador desta solicitação.');
      return;
    }

    const previousRequestIds = this.requestIds();
    try {
      this.capabilityStore.addRequestId(requestId);
      this.requestIds.set(this.capabilityStore.getRequestIds());
    } catch {
      this.recoveryError.set('Não foi possível guardar esta solicitação neste navegador.');
      return;
    }

    if (this.hasStarted && !this.hasSameRequestSet(previousRequestIds)) {
      this.restartStream();
    }
  }

  markRead(requestId: string): void {
    try {
      this.capabilityStore.markRead(requestId);
      this.readIds.set(this.capabilityStore.getReadIds());
    } catch {
      this.recoveryError.set('Não foi possível salvar o estado de leitura neste navegador.');
    }
  }

  isUnread(requestId: string): boolean {
    return !this.readIds().includes(requestId);
  }

  private registerStream(): Promise<void> {
    const requestIds = this.requestIds();
    if (requestIds.length === 0) {
      this.closeStream();
      this.connectionState.set('disconnected');
      return Promise.resolve();
    }

    const version = ++this.streamVersion;
    this.sourceRequestIds = [...requestIds];
    this.connectionState.set('connecting');
    const registration = (async () => {
      try {
        const response = await firstValueFrom(this.api.registerNotificationStream(requestIds));
        if (version !== this.streamVersion || !this.hasSameRequestSet(requestIds)) {
          return;
        }

        const source = this.createEventSource(response.streamUrl);
        this.source = source;
        source.addEventListener('extractionUpdated', (event) => this.acceptEvent(event));
        source.onopen = () => {
          if (this.source !== source) {
            return;
          }
          this.connectionState.set('connected');
          this.recoveryError.set('');
          void this.recoverSavedRequests();
        };
        source.onerror = () => this.handleSourceError(source);
      } catch {
        if (version === this.streamVersion) {
          this.connectionState.set('disconnected');
          this.recoveryError.set('Não foi possível conectar às notificações em tempo real.');
        }
      }
    })();
    this.registration = registration.finally(() => {
      if (version === this.streamVersion) {
        this.registration = undefined;
      }
    });
    return this.registration;
  }

  private handleSourceError(source: AudioExtractionEventSource): void {
    if (this.source !== source) {
      return;
    }
    if (source.readyState === 2) {
      void this.recoverAfterClosedStream(source);
      return;
    }

    this.connectionState.set('connecting');
    this.recoveryError.set('A conexão com as notificações foi interrompida. Tentando reconectar.');
  }

  private async recoverAfterClosedStream(source: AudioExtractionEventSource): Promise<void> {
    if (this.source !== source) {
      return;
    }

    this.closeStream();
    this.connectionState.set('disconnected');
    this.recoveryError.set(
      'A conexão com as notificações foi encerrada. Recuperando solicitações.',
    );
    await this.recoverSavedRequests();
    if (this.requestIds().length > 0 && !this.allTrackedOutcomesAreTerminal()) {
      await this.registerStream();
    }
  }

  private async recoverSavedRequests(): Promise<void> {
    if (this.recovery) {
      this.recoveryRequested = true;
      return this.recovery;
    }

    const recovery = (async () => {
      do {
        this.recoveryRequested = false;
        await Promise.all(this.requestIds().map((requestId) => this.refreshRequest(requestId)));
        this.reconcileStreamAfterRecovery();
      } while (this.recoveryRequested);
    })();
    this.recovery = recovery.finally(() => {
      this.recovery = undefined;
    });
    return this.recovery;
  }

  private async refreshRequest(requestId: string): Promise<void> {
    try {
      const status = await firstValueFrom(this.api.getStatus(requestId));
      if (!isAudioExtractionStatus(status) || status.requestId !== requestId) {
        this.recoveryError.set('Não foi possível recuperar o estado de uma solicitação.');
        return;
      }
      this.acceptStatus(status);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.removeRequestId(requestId);
        this.acceptStatus({
          requestId,
          status: 'expired',
          message: 'Este resultado expirou ou não está disponível.',
          createdAt: new Date().toISOString(),
          result: null,
        });
        return;
      }
      this.recoveryError.set('Não foi possível recuperar o estado de uma solicitação.');
    }
  }

  private acceptEvent(event: Event): void {
    if (!(event instanceof MessageEvent) || typeof event.data !== 'string') {
      return;
    }

    let value: unknown;
    try {
      value = JSON.parse(event.data) as unknown;
    } catch (error) {
      if (error instanceof SyntaxError) {
        return;
      }
      throw error;
    }

    if (!isAudioExtractionStatus(value) || !this.requestIds().includes(value.requestId)) {
      return;
    }
    this.acceptStatus(value);
  }

  private acceptStatus(status: AudioExtractionNotificationOutcome): void {
    if (
      status.status !== 'completed' &&
      status.status !== 'failed' &&
      status.status !== 'expired'
    ) {
      return;
    }
    const current = this.outcomes().filter((item) => item.requestId !== status.requestId);
    this.outcomes.set(
      [...current, status].sort((left, right) => right.createdAt.localeCompare(left.createdAt)),
    );
    this.closeStreamWhenComplete();
  }

  private removeRequestId(requestId: string): void {
    try {
      this.capabilityStore.removeRequestId(requestId);
      this.requestIds.set(this.capabilityStore.getRequestIds());
      this.readIds.set(this.capabilityStore.getReadIds());
    } catch {
      this.recoveryError.set('Não foi possível atualizar as solicitações deste navegador.');
    }
  }

  private reconcileStreamAfterRecovery(): void {
    if (!this.source) {
      return;
    }

    if (this.requestIds().length === 0 || this.allTrackedOutcomesAreTerminal()) {
      this.closeStream();
      this.connectionState.set('disconnected');
      return;
    }
    if (!this.hasSameRequestSet(this.sourceRequestIds)) {
      this.restartStream();
    }
  }

  private closeStreamWhenComplete(): void {
    if (this.requestIds().length > 0 && !this.allTrackedOutcomesAreTerminal()) {
      return;
    }
    this.closeStream();
    this.connectionState.set('disconnected');
  }

  private allTrackedOutcomesAreTerminal(): boolean {
    const outcomes = new Set(
      this.outcomes()
        .filter(
          (outcome) =>
            outcome.status === 'completed' ||
            outcome.status === 'failed' ||
            outcome.status === 'expired',
        )
        .map((outcome) => outcome.requestId),
    );
    return (
      this.requestIds().length > 0 &&
      this.requestIds().every((requestId) => outcomes.has(requestId))
    );
  }

  private hasSameRequestSet(requestIds: string[] = this.sourceRequestIds): boolean {
    const current = this.requestIds();
    return (
      requestIds.length === current.length &&
      requestIds.every((requestId, index) => requestId === current[index])
    );
  }

  private restartStream(): void {
    this.closeStream();
    if (this.requestIds().length > 0) {
      void this.registerStream();
    } else {
      this.connectionState.set('disconnected');
    }
  }

  private closeStream(): void {
    this.streamVersion++;
    this.source?.close();
    this.source = undefined;
    this.sourceRequestIds = [];
    this.registration = undefined;
  }

  private invalidLocalState(): void {
    this.recoveryError.set('Os dados locais de notificações estavam inválidos e foram removidos.');
  }
}

function isAudioExtractionStatus(value: unknown): value is AudioExtractionStatus {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const status = value as Partial<AudioExtractionStatus>;
  if (
    typeof status.requestId !== 'string' ||
    !REQUEST_ID_PATTERN.test(status.requestId) ||
    typeof status.message !== 'string' ||
    typeof status.createdAt !== 'string'
  ) {
    return false;
  }

  const requestId = status.requestId;
  if (status.status === 'completed') {
    return (
      status.result !== null &&
      typeof status.result === 'object' &&
      status.result.audioPath === `/api/audio-extractions/${encodeURIComponent(requestId)}/audio` &&
      Number.isInteger(status.result.durationSeconds) &&
      status.result.durationSeconds > 0 &&
      status.result.contentType === 'audio/mpeg'
    );
  }

  return (status.status === 'accepted' || status.status === 'failed') && status.result === null;
}

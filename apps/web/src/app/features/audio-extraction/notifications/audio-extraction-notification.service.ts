import { HttpErrorResponse } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { PLATFORM_ID } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { AudioExtractionApiService, AudioExtractionStatus } from '../audio-extraction-api.service';
import { RequestCapabilityStore } from './request-capability-store';

const REQUEST_ID_PATTERN = /^[A-Za-z0-9_-]{43}$/;

export interface AudioExtractionHubConnection {
  start(): Promise<void>;
  invoke(methodName: string, requestId: string): Promise<unknown>;
  on(methodName: string, callback: (value: unknown) => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
}

export type AudioExtractionNotificationOutcome =
  AudioExtractionStatus | (Omit<AudioExtractionStatus, 'status'> & { status: 'expired' });

class SignalRConnectionAdapter implements AudioExtractionHubConnection {
  constructor(private readonly connection: HubConnection) {}

  start(): Promise<void> {
    return this.connection.start();
  }

  invoke(methodName: string, requestId: string): Promise<unknown> {
    return this.connection.invoke(methodName, requestId);
  }

  on(methodName: string, callback: (value: unknown) => void): void {
    this.connection.on(methodName, (...arguments_: unknown[]) => callback(arguments_[0]));
  }

  onreconnected(callback: () => void): void {
    this.connection.onreconnected(() => callback());
  }

  onreconnecting(callback: () => void): void {
    this.connection.onreconnecting(() => callback());
  }

  onclose(callback: () => void): void {
    this.connection.onclose(() => callback());
  }
}

export const AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY = new InjectionToken<
  () => AudioExtractionHubConnection
>('AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY', {
  providedIn: 'root',
  factory: () => () =>
    new SignalRConnectionAdapter(
      new HubConnectionBuilder()
        .withUrl('/hubs/audio-extractions')
        .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
        .configureLogging(LogLevel.Error)
        .build(),
    ),
});

@Injectable({ providedIn: 'root' })
export class AudioExtractionNotificationService {
  private readonly api = inject(AudioExtractionApiService);
  private readonly createConnection = inject(AUDIO_EXTRACTION_HUB_CONNECTION_FACTORY);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly capabilityStore = inject(RequestCapabilityStore);
  private readonly requestIds = signal<string[]>([]);
  private readonly readIds = signal<string[]>([]);
  private connection?: AudioExtractionHubConnection;
  private connecting?: Promise<void>;

  readonly outcomes = signal<AudioExtractionNotificationOutcome[]>([]);
  readonly connectionState = signal<'disconnected' | 'connecting' | 'connected'>('disconnected');
  readonly recoveryError = signal('');
  readonly unreadCount = computed(
    () => this.outcomes().filter((outcome) => !this.readIds().includes(outcome.requestId)).length,
  );

  constructor() {
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
    if (this.connectionState() === 'connected' || this.connecting) {
      return this.connecting;
    }

    this.requestIds.set(this.capabilityStore.getRequestIds(() => this.invalidLocalState()));
    this.readIds.set(this.capabilityStore.getReadIds(() => this.invalidLocalState()));
    this.connectionState.set('connecting');
    const connection = this.createConnection();
    this.connection = connection;
    connection.on('extractionUpdated', (value) => this.acceptHubOutcome(value));
    connection.onreconnecting(() => this.connectionState.set('connecting'));
    connection.onreconnected(() => {
      this.connectionState.set('connected');
      void this.recoverSavedRequests();
    });
    connection.onclose(() => {
      this.connectionState.set('disconnected');
      this.recoveryError.set('A conexão com as notificações em tempo real foi encerrada.');
    });

    this.connecting = (async () => {
      try {
        await connection.start();
        this.connectionState.set('connected');
        this.recoveryError.set('');
        await this.recoverSavedRequests();
      } catch {
        this.connectionState.set('disconnected');
        this.recoveryError.set('Não foi possível conectar às notificações em tempo real.');
      } finally {
        this.connecting = undefined;
      }
    })();
    return this.connecting;
  }

  trackRequest(requestId: string): void {
    if (!REQUEST_ID_PATTERN.test(requestId)) {
      this.recoveryError.set('Não foi possível guardar o identificador desta solicitação.');
      return;
    }

    try {
      this.capabilityStore.addRequestId(requestId);
      this.requestIds.set(this.capabilityStore.getRequestIds());
    } catch {
      this.recoveryError.set('Não foi possível guardar esta solicitação neste navegador.');
      return;
    }

    if (this.connectionState() === 'connected') {
      void this.refreshRequest(requestId);
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

  private async recoverSavedRequests(): Promise<void> {
    const requestIds = this.requestIds();
    await Promise.all(requestIds.map((requestId) => this.refreshRequest(requestId)));
  }

  private async refreshRequest(requestId: string): Promise<void> {
    try {
      await this.connection?.invoke('Subscribe', requestId);
    } catch {
      this.recoveryError.set('Não foi possível assinar uma atualização em tempo real.');
    }

    try {
      const status = await firstValueFrom(this.api.getStatus(requestId));
      if (!isAudioExtractionStatus(status)) {
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

  private acceptHubOutcome(value: unknown): void {
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

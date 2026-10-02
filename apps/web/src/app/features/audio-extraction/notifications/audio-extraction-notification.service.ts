import { HttpErrorResponse } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { computed, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { PLATFORM_ID } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { AudioExtractionApiService, AudioExtractionStatus } from '../audio-extraction-api.service';

const REQUEST_IDS_KEY = 'taskpilot.audio-extraction.request-ids';
const READ_IDS_KEY = 'taskpilot.audio-extraction.read-ids';
const REQUEST_ID_PATTERN = /^[A-Za-z0-9_-]{43}$/;

export interface AudioExtractionHubConnection {
  start(): Promise<void>;
  invoke(methodName: string, requestId: string): Promise<unknown>;
  on(methodName: string, callback: (value: unknown) => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
}

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
  private readonly requestIds = signal<string[]>([]);
  private readonly readIds = signal<string[]>([]);
  private connection?: AudioExtractionHubConnection;
  private connecting?: Promise<void>;

  readonly outcomes = signal<AudioExtractionStatus[]>([]);
  readonly connectionState = signal<'disconnected' | 'connecting' | 'connected'>('disconnected');
  readonly recoveryError = signal('');
  readonly unreadCount = computed(
    () => this.outcomes().filter((outcome) => !this.readIds().includes(outcome.requestId)).length,
  );

  constructor() {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }
    this.requestIds.set(this.readIdsFromStorage(REQUEST_IDS_KEY));
    this.readIds.set(this.readIdsFromStorage(READ_IDS_KEY));
  }

  async connect(): Promise<void> {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }
    if (this.connectionState() === 'connected' || this.connecting) {
      return this.connecting;
    }

    this.requestIds.set(this.readIdsFromStorage(REQUEST_IDS_KEY));
    this.readIds.set(this.readIdsFromStorage(READ_IDS_KEY));
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

    const requestIds = [...new Set([...this.requestIds(), requestId])];
    this.requestIds.set(requestIds);
    try {
      localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify(requestIds));
    } catch {
      this.recoveryError.set('Não foi possível guardar esta solicitação neste navegador.');
      return;
    }

    if (this.connectionState() === 'connected') {
      void this.refreshRequest(requestId);
    }
  }

  markRead(requestId: string): void {
    const readIds = [...new Set([...this.readIds(), requestId])];
    this.readIds.set(readIds);
    try {
      localStorage.setItem(READ_IDS_KEY, JSON.stringify(readIds));
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
      this.acceptStatus(status);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.removeRequestId(requestId);
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

  private acceptStatus(status: AudioExtractionStatus): void {
    if (status.status !== 'completed' && status.status !== 'failed') {
      return;
    }
    const current = this.outcomes().filter((item) => item.requestId !== status.requestId);
    this.outcomes.set(
      [...current, status].sort((left, right) => right.createdAt.localeCompare(left.createdAt)),
    );
  }

  private removeRequestId(requestId: string): void {
    const requestIds = this.requestIds().filter((item) => item !== requestId);
    this.requestIds.set(requestIds);
    try {
      localStorage.setItem(REQUEST_IDS_KEY, JSON.stringify(requestIds));
    } catch {
      this.recoveryError.set('Não foi possível atualizar as solicitações deste navegador.');
    }
  }

  private readIdsFromStorage(key: string): string[] {
    const saved = localStorage.getItem(key);
    if (!saved) {
      return [];
    }
    try {
      const value: unknown = JSON.parse(saved);
      if (!Array.isArray(value) || !value.every((item) => typeof item === 'string')) {
        throw new SyntaxError('Invalid request ID storage.');
      }
      return value.filter((item) => REQUEST_ID_PATTERN.test(item));
    } catch (error) {
      if (!(error instanceof SyntaxError)) {
        throw error;
      }
      localStorage.removeItem(key);
      this.recoveryError.set(
        'Os dados locais de notificações estavam inválidos e foram removidos.',
      );
      return [];
    }
  }
}

function isAudioExtractionStatus(value: unknown): value is AudioExtractionStatus {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const status = value as Partial<AudioExtractionStatus>;
  return (
    typeof status.requestId === 'string' &&
    REQUEST_ID_PATTERN.test(status.requestId) &&
    (status.status === 'completed' || status.status === 'failed') &&
    typeof status.message === 'string' &&
    typeof status.createdAt === 'string' &&
    (status.result === null ||
      (typeof status.result === 'object' &&
        typeof status.result.audioPath === 'string' &&
        typeof status.result.durationSeconds === 'number' &&
        status.result.contentType === 'audio/mpeg'))
  );
}

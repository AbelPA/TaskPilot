import { Injectable } from '@angular/core';

const REQUEST_IDS_KEY = 'taskpilot.audio-extraction.request-ids';
const READ_IDS_KEY = 'taskpilot.audio-extraction.read-ids';
const REQUEST_ID_PATTERN = /^[A-Za-z0-9_-]{43}$/;

@Injectable({ providedIn: 'root' })
export class RequestCapabilityStore {
  getRequestIds(onInvalidState?: () => void): string[] {
    return this.readIds(REQUEST_IDS_KEY, onInvalidState);
  }

  getReadIds(onInvalidState?: () => void): string[] {
    return this.readIds(READ_IDS_KEY, onInvalidState);
  }

  addRequestId(requestId: string): void {
    if (!REQUEST_ID_PATTERN.test(requestId)) {
      throw new TypeError('A valid request capability is required.');
    }
    const requestIds = this.getRequestIds();
    this.writeIds(REQUEST_IDS_KEY, [...new Set([...requestIds, requestId])]);
  }

  markRead(requestId: string): void {
    if (!REQUEST_ID_PATTERN.test(requestId)) {
      throw new TypeError('A valid request capability is required.');
    }
    const readIds = this.getReadIds();
    this.writeIds(READ_IDS_KEY, [...new Set([...readIds, requestId])]);
  }

  removeRequestId(requestId: string): void {
    this.writeIds(
      REQUEST_IDS_KEY,
      this.getRequestIds().filter((item) => item !== requestId),
    );
    this.writeIds(
      READ_IDS_KEY,
      this.getReadIds().filter((item) => item !== requestId),
    );
  }

  private readIds(key: string, onInvalidState?: () => void): string[] {
    const saved = localStorage.getItem(key);
    if (!saved) {
      return [];
    }
    try {
      const value: unknown = JSON.parse(saved);
      if (!Array.isArray(value) || !value.every((item) => typeof item === 'string')) {
        throw new SyntaxError('Invalid request capability storage.');
      }
      const requestIds = [...new Set(value.filter((item) => REQUEST_ID_PATTERN.test(item)))];
      if (requestIds.length !== value.length) {
        this.writeIds(key, requestIds);
        onInvalidState?.();
      }
      return requestIds;
    } catch (error) {
      if (!(error instanceof SyntaxError)) {
        throw error;
      }
      localStorage.removeItem(key);
      onInvalidState?.();
      return [];
    }
  }

  private writeIds(key: string, values: string[]): void {
    localStorage.setItem(key, JSON.stringify(values));
  }
}

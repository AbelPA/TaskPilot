import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface YouTubeApiKeyStatus {
  configured: boolean;
  source: 'database' | 'environment' | 'none';
  updatedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class SettingsApiService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/settings/youtube-api-key';

  getYouTubeApiKeyStatus(): Observable<YouTubeApiKeyStatus> {
    return this.http.get<YouTubeApiKeyStatus>(this.url);
  }

  saveYouTubeApiKey(apiKey: string): Observable<YouTubeApiKeyStatus> {
    return this.http.put<YouTubeApiKeyStatus>(this.url, { apiKey });
  }

  removeYouTubeApiKey(): Observable<YouTubeApiKeyStatus> {
    return this.http.delete<YouTubeApiKeyStatus>(this.url);
  }
}

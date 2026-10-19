import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface YouTubeApiKeyStatus {
  configured: boolean;
  source: 'database' | 'environment' | 'none';
  updatedAt: string | null;
  maskedValue?: string | null;
}

export interface YouTubeAccountStatus {
  configured: boolean;
  maskedUsername: string | null;
  maskedPassword: string | null;
  updatedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class SettingsApiService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/settings/youtube-api-key';
  private readonly accountUrl = '/api/settings/youtube-account';

  getYouTubeApiKeyStatus(): Observable<YouTubeApiKeyStatus> {
    return this.http.get<YouTubeApiKeyStatus>(this.url);
  }

  saveYouTubeApiKey(apiKey: string): Observable<YouTubeApiKeyStatus> {
    return this.http.put<YouTubeApiKeyStatus>(this.url, { apiKey });
  }

  removeYouTubeApiKey(): Observable<YouTubeApiKeyStatus> {
    return this.http.delete<YouTubeApiKeyStatus>(this.url);
  }

  getYouTubeAccountStatus(): Observable<YouTubeAccountStatus> {
    return this.http.get<YouTubeAccountStatus>(this.accountUrl);
  }

  saveYouTubeAccount(username: string, password: string): Observable<YouTubeAccountStatus> {
    return this.http.put<YouTubeAccountStatus>(this.accountUrl, { username, password });
  }

  removeYouTubeAccount(): Observable<YouTubeAccountStatus> {
    return this.http.delete<YouTubeAccountStatus>(this.accountUrl);
  }
}

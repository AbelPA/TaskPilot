import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SettingsApiService } from './settings-api.service';

describe('SettingsApiService', () => {
  let api: SettingsApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(SettingsApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the key status', () => {
    const status = { configured: true, source: 'database', updatedAt: null };
    api.getYouTubeApiKeyStatus().subscribe(result => expect(result).toEqual(status));
    http.expectOne({ method: 'GET', url: '/api/settings/youtube-api-key' }).flush(status);
  });

  it('sends the key in the request body, not in the URL', () => {
    api.saveYouTubeApiKey('secret-key-1234567890').subscribe();
    const request = http.expectOne({ method: 'PUT', url: '/api/settings/youtube-api-key' });
    expect(request.request.body).toEqual({ apiKey: 'secret-key-1234567890' });
    request.flush({ configured: true, source: 'database', updatedAt: null });
  });

  it('removes the stored key', () => {
    api.removeYouTubeApiKey().subscribe();
    http
      .expectOne({ method: 'DELETE', url: '/api/settings/youtube-api-key' })
      .flush({ configured: false, source: 'none', updatedAt: null });
  });

  it('sends the account credentials in the request body, not in the URL', () => {
    api.saveYouTubeAccount('conta@example.com', 'Sup3r-secret').subscribe();
    const request = http.expectOne({ method: 'PUT', url: '/api/settings/youtube-account' });
    expect(request.request.body).toEqual({ username: 'conta@example.com', password: 'Sup3r-secret' });
    request.flush({ configured: true, maskedUsername: 'co***ta@example.com', maskedPassword: '***', updatedAt: null });
  });
});

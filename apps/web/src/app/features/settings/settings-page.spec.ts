import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SettingsApiService, YouTubeAccountStatus, YouTubeApiKeyStatus } from './settings-api.service';
import { SettingsPage } from './settings-page';

const none: YouTubeApiKeyStatus = { configured: false, source: 'none', updatedAt: null, maskedValue: null };
const stored: YouTubeApiKeyStatus = {
  configured: true,
  source: 'database',
  updatedAt: '2026-10-16T19:00:00Z',
  maskedValue: 'AIzaS*********Vsw',
};

const noAccount: YouTubeAccountStatus = {
  configured: false,
  maskedUsername: null,
  maskedPassword: null,
  updatedAt: null,
};

const configuredAccount: YouTubeAccountStatus = {
  configured: true,
  maskedUsername: 'tp***pa@gmail.com',
  maskedPassword: '*********************',
  updatedAt: '2026-10-16T19:00:00Z',
};

async function render(api: Partial<SettingsApiService>) {
  await TestBed.configureTestingModule({
    imports: [SettingsPage],
    providers: [
      {
        provide: SettingsApiService,
        useValue: {
          getYouTubeAccountStatus: () => of(noAccount),
          ...api,
        },
      },
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(SettingsPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('SettingsPage', () => {
  it('shows that no key is configured', async () => {
    const fixture = await render({ getYouTubeApiKeyStatus: () => of(none) });

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Nenhuma chave configurada');
    expect(element.querySelector('.secondary-button')).toBeNull();
    expect(element.querySelector<HTMLInputElement>('#youtube-api-key')?.value).toBe('');
    expect(element.querySelector<HTMLInputElement>('#youtube-account-username')?.value).toBe('');
    expect(element.querySelector<HTMLInputElement>('#youtube-account-password')?.value).toBe('');
  });

  it('shows the configured values masked in the fields', async () => {
    const fixture = await render({
      getYouTubeApiKeyStatus: () => of(stored),
      getYouTubeAccountStatus: () => of(configuredAccount),
    });

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector<HTMLInputElement>('#youtube-api-key')!.value).toBe('AIzaS*********Vsw');
    expect(element.querySelector<HTMLInputElement>('#youtube-account-username')!.value).toBe('tp***pa@gmail.com');
    expect(element.querySelector<HTMLInputElement>('#youtube-account-password')!.value).toBe('*********************');
    element.querySelectorAll<HTMLButtonElement>('[type="submit"]').forEach(button => expect(button.disabled).toBe(true));
  });

  it('saves the key and shows only its masked value afterwards', async () => {
    const save = vi.fn(() => of(stored));
    const fixture = await render({ getYouTubeApiKeyStatus: () => of(none), saveYouTubeApiKey: save });
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector<HTMLInputElement>('#youtube-api-key')!;

    input.value = '  secret-key-1234567890  ';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(save).toHaveBeenCalledWith('secret-key-1234567890');
    expect(element.textContent).toContain('Chave salva com segurança.');
    expect(element.textContent).toContain('criptografada');
    expect(element.textContent).not.toContain('secret-key-1234567890');
    expect(input.value).toBe('AIzaS*********Vsw');
  });

  it('disables saving while the field is empty', async () => {
    const fixture = await render({ getYouTubeApiKeyStatus: () => of(none) });

    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[type="submit"]');
    expect(button?.disabled).toBe(true);
  });

  it('shows the server problem detail when saving fails', async () => {
    const fixture = await render({
      getYouTubeApiKeyStatus: () => of(none),
      saveYouTubeApiKey: () =>
        throwError(() => new HttpErrorResponse({ status: 400, error: { detail: 'Chave inválida.' } })),
    });
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector<HTMLInputElement>('#youtube-api-key')!;

    input.value = 'bad';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Chave inválida.');
  });

  it('removes a stored key', async () => {
    const remove = vi.fn(() => of(none));
    const fixture = await render({ getYouTubeApiKeyStatus: () => of(stored), removeYouTubeApiKey: remove });
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector<HTMLButtonElement>('.secondary-button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(remove).toHaveBeenCalled();
    expect(element.textContent).toContain('Chave removida.');
    expect(element.querySelector('.secondary-button')).toBeNull();
    expect(element.querySelector<HTMLInputElement>('#youtube-api-key')!.value).toBe('');
  });

  it('offers a retry when loading fails', async () => {
    const fixture = await render({
      getYouTubeApiKeyStatus: () => throwError(() => new HttpErrorResponse({ status: 500 })),
    });

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Não foi possível carregar');
    expect(element.querySelector('.primary-button')?.textContent).toContain('Tentar novamente');
  });

  it('saves the YouTube account and shows its masked values afterwards', async () => {
    const save = vi.fn(() =>
      of({ ...configuredAccount, maskedUsername: 'co***ta@example.com' }),
    );
    const fixture = await render({ getYouTubeApiKeyStatus: () => of(none), saveYouTubeAccount: save });
    const element = fixture.nativeElement as HTMLElement;
    const username = element.querySelector<HTMLInputElement>('#youtube-account-username')!;
    const password = element.querySelector<HTMLInputElement>('#youtube-account-password')!;

    username.value = ' conta@example.com ';
    username.dispatchEvent(new Event('input'));
    password.value = 'Sup3r-secret';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    element.querySelectorAll<HTMLFormElement>('form')[1].dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(password.type).toBe('password');
    expect(save).toHaveBeenCalledWith('conta@example.com', 'Sup3r-secret');
    expect(username.value).toBe('co***ta@example.com');
    expect(element.textContent).not.toContain('Sup3r-secret');
    expect(password.value).toBe('*********************');
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NEVER, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AudioExtractionApiService } from './audio-extraction-api.service';
import { AudioExtractionPage } from './audio-extraction-page';

describe('AudioExtractionPage', () => {
  let fixture: ComponentFixture<AudioExtractionPage>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AudioExtractionPage],
      providers: [{ provide: AudioExtractionApiService, useValue: { submit: vi.fn() } }],
    }).compileComponents();
    fixture = TestBed.createComponent(AudioExtractionPage);
    fixture.detectChanges();
  });

  it('requires URL and time fields', () => {
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Informe o link do vídeo.');
    expect(fixture.nativeElement.textContent).toContain('Informe o horário inicial.');
    expect(fixture.nativeElement.textContent).toContain('Informe o horário final.');
  });

  it('rejects unsupported URLs and invalid intervals before calling the API', () => {
    const inputs = fixture.nativeElement.querySelectorAll('input') as NodeListOf<HTMLInputElement>;
    inputs[0].value = 'https://example.com/watch?v=abcdefghijk';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = '00:02:00';
    inputs[1].dispatchEvent(new Event('input'));
    inputs[2].value = '00:01:00';
    inputs[2].dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Informe um link válido de vídeo do YouTube.',
    );
    expect(fixture.nativeElement.textContent).toContain('O fim deve ser posterior ao início.');
    expect(TestBed.inject(AudioExtractionApiService).submit).not.toHaveBeenCalled();
  });

  it('rejects an interval longer than 30 minutes', () => {
    const inputs = fixture.nativeElement.querySelectorAll('input') as NodeListOf<HTMLInputElement>;
    inputs[0].value = 'https://youtu.be/abcdefghijk';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = '00:00:00';
    inputs[1].dispatchEvent(new Event('input'));
    inputs[2].value = '00:30:01';
    inputs[2].dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('O trecho não pode exceder 30 minutos.');
    expect(TestBed.inject(AudioExtractionApiService).submit).not.toHaveBeenCalled();
  });

  it('rejects timestamps outside HH:MM:SS format', () => {
    const inputs = fixture.nativeElement.querySelectorAll('input') as NodeListOf<HTMLInputElement>;
    inputs[0].value = 'https://youtu.be/abcdefghijk';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = '0:00:00';
    inputs[1].dispatchEvent(new Event('input'));
    inputs[2].value = '00:00:30';
    inputs[2].dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Use o formato HH:MM:SS.');
    expect(TestBed.inject(AudioExtractionApiService).submit).not.toHaveBeenCalled();
  });

  it('disables duplicate submission while the request is pending', () => {
    const api = TestBed.inject(AudioExtractionApiService);
    vi.mocked(api.submit).mockReturnValue(NEVER);
    const inputs = fixture.nativeElement.querySelectorAll('input') as NodeListOf<HTMLInputElement>;
    inputs[0].value = 'https://youtu.be/abcdefghijk';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = '00:00:00';
    inputs[1].dispatchEvent(new Event('input'));
    inputs[2].value = '00:00:30';
    inputs[2].dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(api.submit).toHaveBeenCalledTimes(1);
    expect((fixture.nativeElement.querySelector('button') as HTMLButtonElement).disabled).toBe(
      true,
    );
  });

  it('shows safe API errors such as the per-IP rate limit response', () => {
    const api = TestBed.inject(AudioExtractionApiService);
    vi.mocked(api.submit).mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 429,
            error: { title: 'Rate limit exceeded', detail: 'Aguarde um minuto.' },
          }),
      ),
    );
    const inputs = fixture.nativeElement.querySelectorAll('input') as NodeListOf<HTMLInputElement>;
    inputs[0].value = 'https://youtu.be/abcdefghijk';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = '00:00:00';
    inputs[1].dispatchEvent(new Event('input'));
    inputs[2].value = '00:00:30';
    inputs[2].dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Aguarde um minuto.');
  });
});

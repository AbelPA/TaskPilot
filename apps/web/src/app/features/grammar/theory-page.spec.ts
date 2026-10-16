import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { GrammarApiService } from './grammar-api.service';
import { TheoryPage } from './theory-page';

describe('TheoryPage', () => {
  it('renders typed theory blocks as inert text and offers exercise practice', async () => {
    await TestBed.configureTestingModule({
      imports: [TheoryPage],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ slug: 'present-simple' })) },
        },
        {
          provide: GrammarApiService,
          useValue: {
            getTopic: () => of({
              id: 'topic-1',
              slug: 'present-simple',
              title: 'Present Simple',
              description: 'Verb to be',
              level: 'A1',
              displayOrder: 1,
              exerciseCount: 1,
              previousTopicSlug: null,
              nextTopicSlug: null,
              theorySections: [{
                title: 'Forms',
                displayOrder: 1,
                blocks: [
                  { type: 'paragraph', text: '<script>not executable</script>' },
                  { type: 'example', text: 'I am a student.', translation: 'Sou estudante.' },
                  { type: 'list', ordered: false, items: ['Use am with I.'] },
                  { type: 'table', headers: ['Subject', 'Verb'], rows: [['I', 'am']] },
                ],
              }],
            }),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(TheoryPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('script')).toBeNull();
    expect(page.textContent).toContain('<script>not executable</script>');
    expect(page.textContent).toContain('Sou estudante.');
    expect(page.querySelector('table')).not.toBeNull();
    expect(page.querySelector('a[href="/grammar/present-simple/practice"]')).not.toBeNull();
  });
});

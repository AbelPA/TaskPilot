import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { GrammarApiService } from './grammar-api.service';

describe('GrammarApiService', () => {
  let api: GrammarApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(GrammarApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads active topics from the grammar API', () => {
    const topics = [{ id: 'topic-1', slug: 'present-simple', title: 'Present Simple' }];
    api.getTopics().subscribe(result => expect(result).toEqual(topics));
    http.expectOne('/api/grammar/topics').flush(topics);
  });

  it('posts blank and multiple-choice answers for server-side correction', () => {
    const submission = {
      blankAnswers: [{ blankKey: '1', answer: 'are' }],
      questionAnswers: [{ questionKey: '1', optionKey: 'B' }],
    };
    api.submit('exercise-1', submission).subscribe(result => expect(result.score).toBe(2));
    const request = http.expectOne('/api/grammar/exercises/exercise-1/submit');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(submission);
    request.flush({
      exerciseId: 'exercise-1',
      score: 2,
      maxScore: 2,
      percentage: 100,
      explanation: 'Good work.',
      results: [],
    });
  });
});

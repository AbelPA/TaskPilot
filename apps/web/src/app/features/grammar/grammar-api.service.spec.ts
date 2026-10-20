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
    api.getTopics().subscribe((result) => expect(result).toEqual(topics));
    http.expectOne('/api/grammar/topics').flush(topics);
  });

  it('posts blank and multiple-choice answers for server-side correction', () => {
    const submission = {
      submissionId: 'submission-1',
      topicId: 'topic-1',
      blankAnswers: [{ blankKey: '1', answer: 'are' }],
      questionAnswers: [{ questionKey: '1', optionKey: 'B' }],
    };
    api.submit('exercise-1', submission).subscribe((result) => expect(result.score).toBe(2));
    const request = http.expectOne('/api/grammar/exercises/exercise-1/submit');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(submission);
    request.flush({
      submissionId: 'submission-1',
      attemptNumber: 1,
      exerciseId: 'exercise-1',
      score: 2,
      maxScore: 2,
      percentage: 100,
      explanation: 'Good work.',
      results: [],
    });
  });

  it('loads topic statistics using the checked-in statistics endpoint', () => {
    const statistics = [
      {
        topicId: 'topic-1',
        topicSlug: 'present-simple',
        topicTitle: 'Present Simple',
        totalAttempts: 0,
        correctAnswers: 0,
        incorrectAnswers: 0,
        accuracyPercentage: null,
        errorPercentage: null,
        lastAttemptAt: null,
        recentAttemptCount: 0,
        recentCorrectAnswers: 0,
        recentIncorrectAnswers: 0,
        assessed: false,
        insufficientEvidence: true,
      },
    ];
    api.getTopicStatistics().subscribe((result) => expect(result).toEqual(statistics));
    http.expectOne('/api/grammar/statistics/topics').flush(statistics);
  });

  it('creates review sessions without client-supplied selection controls', () => {
    const session = {
      sessionId: 'session-1',
      standardExerciseCount: 10,
      exerciseCount: 0,
      isShorterThanStandard: true,
      exercises: [],
    };
    api.createReviewSession().subscribe((result) => expect(result).toEqual(session));
    const request = http.expectOne('/api/grammar/review/sessions');
    expect(request.request.method).toBe('POST');
    request.flush(session);
  });

  it('loads a review summary for the exact session identifier', () => {
    const summary = {
      sessionId: 'session-1',
      exercisesAnswered: 1,
      correctAnswers: 2,
      incorrectAnswers: 1,
      accuracyPercentage: 66.7,
      topicResults: [],
      priorComparisonAvailable: false,
      priorAccuracyPercentage: null,
    };
    api.getReviewSummary('session-1').subscribe((result) => expect(result).toEqual(summary));
    http.expectOne('/api/grammar/review/sessions/session-1/summary').flush(summary);
  });
});

using Api.Grammar.Contracts;
using Api.Grammar.Services;
using Api.Grammar.Validation;
using Microsoft.AspNetCore.Mvc;

namespace Api.Grammar.Endpoints;

public static class GrammarEndpoints
{
    public static IEndpointRouteBuilder MapGrammarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/grammar").WithTags("Grammar study");

        group.MapGet("/statistics/topics", GetTopicStatisticsAsync)
            .WithName("GetGrammarTopicStatistics")
            .Produces<IReadOnlyList<GrammarTopicStatisticsResponse>>();
        group.MapPost("/review/sessions", CreateReviewSessionAsync)
            .WithName("CreateGrammarReviewSession")
            .Produces<GrammarReviewSessionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        group.MapGet("/review/sessions/{sessionId:guid}/summary", GetReviewSummaryAsync)
            .WithName("GetGrammarReviewSessionSummary")
            .Produces<GrammarReviewSessionSummaryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/topics", GetTopicsAsync)
            .WithName("GetGrammarTopics")
            .Produces<IReadOnlyList<GrammarTopicSummary>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapGet("/topics/{slug}", GetTopicAsync)
            .WithName("GetGrammarTopic")
            .Produces<GrammarTopicResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        group.MapGet("/topics/{slug}/exercises", GetExercisesAsync)
            .WithName("GetGrammarTopicExercises")
            .Produces<IReadOnlyList<GrammarExerciseSummary>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/exercises/{exerciseId:guid}", GetExerciseAsync)
            .WithName("GetGrammarExercise")
            .Produces<GrammarExerciseResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        group.MapPost("/exercises/{exerciseId:guid}/submit", SubmitExerciseAsync)
            .WithName("SubmitGrammarExercise")
            .Accepts<GrammarExerciseSubmission>("application/json")
            .Produces<GrammarExerciseResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static async Task<IResult> GetTopicStatisticsAsync(GrammarStatisticsService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetTopicStatisticsAsync(cancellationToken));

    private static async Task<IResult> CreateReviewSessionAsync(GrammarReviewService service, CancellationToken cancellationToken)
    {
        var session = await service.CreateSessionAsync(cancellationToken);
        return Results.Json(session, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetReviewSummaryAsync(Guid sessionId, GrammarReviewSummaryService service, CancellationToken cancellationToken)
    {
        var summary = await service.GetSummaryAsync(sessionId, cancellationToken);
        return summary is null ? NotFound("Grammar review session not found.", "GRAMMAR_REVIEW_SESSION_NOT_FOUND") : Results.Ok(summary);
    }

    private static async Task<IResult> GetTopicsAsync(
        GrammarCatalogService service,
        CancellationToken cancellationToken)
    {
        var topics = await service.GetTopicsAsync(cancellationToken);
        return Results.Ok(topics);
    }

    private static async Task<IResult> GetTopicAsync(
        string slug,
        GrammarCatalogService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var topic = await service.GetTopicAsync(slug, cancellationToken);
            return topic is null
                ? NotFound("Grammar topic not found.", "GRAMMAR_TOPIC_NOT_FOUND")
                : Results.Ok(topic);
        }
        catch (GrammarContentException exception)
        {
            loggerFactory.CreateLogger("GrammarEndpoints").LogError(
                exception,
                "Invalid theory content for grammar topic {TopicSlug}.",
                slug);
            return InvalidContent();
        }
    }

    private static async Task<IResult> GetExercisesAsync(
        string slug,
        GrammarCatalogService service,
        CancellationToken cancellationToken)
    {
        var exercises = await service.GetExercisesAsync(slug, cancellationToken);
        return exercises is null
            ? NotFound("Grammar topic not found.", "GRAMMAR_TOPIC_NOT_FOUND")
            : Results.Ok(exercises);
    }

    private static async Task<IResult> GetExerciseAsync(
        Guid exerciseId,
        GrammarExerciseService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var exercise = await service.GetExerciseAsync(exerciseId, cancellationToken);
            return exercise is null
                ? NotFound("Grammar exercise not found.", "GRAMMAR_EXERCISE_NOT_FOUND")
                : Results.Ok(exercise);
        }
        catch (GrammarContentException exception)
        {
            loggerFactory.CreateLogger("GrammarEndpoints").LogError(
                exception,
                "Invalid active grammar exercise {ExerciseId}.",
                exerciseId);
            return InvalidContent();
        }
    }

    private static async Task<IResult> SubmitExerciseAsync(
        Guid exerciseId,
        [FromBody] GrammarExerciseSubmission? submission,
        GrammarExerciseService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (submission is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid grammar exercise submission",
                detail: "Provide a valid answer payload.",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_SUBMISSION" });
        }

        try
        {
            var result = await service.SubmitAsync(exerciseId, submission, cancellationToken);
            return result is null
                ? NotFound("Grammar exercise not found.", "GRAMMAR_EXERCISE_NOT_FOUND")
                : Results.Ok(result);
        }
        catch (GrammarSubmissionConflictException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Submission ID conflict", detail: exception.Message, extensions: new Dictionary<string, object?> { ["code"] = "SUBMISSION_ID_CONFLICT" });
        }
        catch (GrammarSubmissionException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid grammar exercise submission",
                detail: exception.Message,
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_SUBMISSION" });
        }
        catch (GrammarContentException exception)
        {
            loggerFactory.CreateLogger("GrammarEndpoints").LogError(
                exception,
                "Invalid active grammar exercise {ExerciseId} during submission.",
                exerciseId);
            return InvalidContent();
        }
    }

    private static IResult NotFound(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Grammar content not found",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult InvalidContent() =>
        Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Grammar content is unavailable",
            detail: "The configured grammar content is invalid. Please try again later.",
            extensions: new Dictionary<string, object?> { ["code"] = "GRAMMAR_CONTENT_INVALID" });
}

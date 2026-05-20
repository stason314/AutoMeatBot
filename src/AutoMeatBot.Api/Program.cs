using AutoMeatBot.Api.Data;
using AutoMeatBot.Api.Domain;
using AutoMeatBot.Api.Dtos;
using AutoMeatBot.Api.Options;
using AutoMeatBot.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<MeetingExtractionOptions>(builder.Configuration.GetSection("MeetingExtraction"));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? builder.Configuration["ConnectionStrings:Postgres"];

    options.UseNpgsql(connectionString);
});

builder.Services.AddHttpClient<IMeetingExtractor, OllamaMeetingExtractor>();
builder.Services.AddScoped<TelegramWebhookService>();
builder.Services.AddScoped<MeetingAnalysisService>();
builder.Services.AddScoped<MeetingMutationService>();

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/telegram/webhook", async (
    JsonElement update,
    TelegramWebhookService webhookService,
    CancellationToken cancellationToken) =>
{
    await webhookService.HandleAsync(update, cancellationToken);
    return Results.Ok();
});

app.MapGet("/api/meetings", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var meetings = await db.MeetingCandidates
        .AsNoTracking()
        .Include(meeting => meeting.Chat)
        .Include(meeting => meeting.Participants)
        .ThenInclude(participant => participant.TelegramUser)
        .OrderByDescending(meeting => meeting.UpdatedAtUtc)
        .Select(meeting => new MeetingDto(
            meeting.Id,
            meeting.Status.ToString(),
            meeting.Topic,
            meeting.ProposedStartUtc,
            meeting.TimeZone,
            meeting.MeetingUrl,
            meeting.Confidence,
            meeting.AiReason,
            meeting.ChatId,
            meeting.Chat != null ? meeting.Chat.Title : null,
            meeting.SourceFirstMessageId,
            meeting.SourceLastMessageId,
            meeting.CreatedAtUtc,
            meeting.UpdatedAtUtc,
            meeting.Participants
                .OrderBy(participant => participant.DisplayName ?? participant.TelegramUsername)
                .Select(participant => new MeetingParticipantDto(
                    participant.Id,
                    participant.TelegramUserId,
                    participant.TelegramUsername,
                    participant.DisplayName,
                    participant.Email,
                    participant.Role,
                    participant.Response.ToString()))
                .ToList()))
        .ToListAsync(cancellationToken);

    return Results.Ok(meetings);
});

app.MapPatch("/api/meetings/{id:guid}", async Task<IResult> (
    Guid id,
    MeetingUpdateRequest request,
    MeetingMutationService mutations,
    CancellationToken cancellationToken) =>
{
    var meeting = await mutations.UpdateMeetingAsync(id, request, cancellationToken);
    if (meeting is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(meeting);
});

app.MapPost("/api/meetings/{id:guid}/approve", async Task<IResult> (
    Guid id,
    MeetingMutationService mutations,
    CancellationToken cancellationToken) =>
{
    var meeting = await mutations.SetStatusAsync(id, MeetingStatus.Approved, cancellationToken);
    if (meeting is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(meeting);
});

app.MapPost("/api/meetings/{id:guid}/cancel", async Task<IResult> (
    Guid id,
    MeetingMutationService mutations,
    CancellationToken cancellationToken) =>
{
    var meeting = await mutations.SetStatusAsync(id, MeetingStatus.Cancelled, cancellationToken);
    if (meeting is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(meeting);
});

app.MapPost("/api/meetings/{id:guid}/participants", async Task<IResult> (
    Guid id,
    ParticipantCreateRequest request,
    MeetingMutationService mutations,
    CancellationToken cancellationToken) =>
{
    var participant = await mutations.AddParticipantAsync(id, request, cancellationToken);
    if (participant is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(participant);
});

app.MapGet("/api/people", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var people = await db.UserEmailMappings
        .AsNoTracking()
        .Include(mapping => mapping.TelegramUser)
        .OrderBy(mapping => mapping.TelegramUsername)
        .ThenBy(mapping => mapping.DisplayName)
        .Select(mapping => new PersonMappingDto(
            mapping.Id,
            mapping.TelegramUserId,
            mapping.TelegramUsername,
            mapping.TelegramUser != null ? mapping.TelegramUser.DisplayName : mapping.DisplayName,
            mapping.Email,
            mapping.Source,
            mapping.UpdatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(people);
});

app.MapPost("/api/people", async (
    PersonMappingCreateRequest request,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var mapping = new UserEmailMapping
    {
        TelegramUsername = NormalizeUsername(request.TelegramUsername),
        DisplayName = request.DisplayName?.Trim(),
        Email = request.Email.Trim(),
        Source = "manual",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    db.UserEmailMappings.Add(mapping);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new PersonMappingDto(
        mapping.Id,
        mapping.TelegramUserId,
        mapping.TelegramUsername,
        mapping.DisplayName,
        mapping.Email,
        mapping.Source,
        mapping.UpdatedAtUtc));
});

app.MapPatch("/api/people/{id:guid}", async Task<IResult> (
    Guid id,
    PersonMappingUpdateRequest request,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var mapping = await db.UserEmailMappings.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (mapping is null)
    {
        return Results.NotFound();
    }

    if (request.TelegramUsername is not null)
    {
        mapping.TelegramUsername = NormalizeUsername(request.TelegramUsername);
    }

    if (request.DisplayName is not null)
    {
        mapping.DisplayName = request.DisplayName.Trim();
    }

    if (request.Email is not null)
    {
        mapping.Email = request.Email.Trim();
    }

    mapping.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new PersonMappingDto(
        mapping.Id,
        mapping.TelegramUserId,
        mapping.TelegramUsername,
        mapping.DisplayName,
        mapping.Email,
        mapping.Source,
        mapping.UpdatedAtUtc));
});

app.MapFallbackToFile("index.html");

app.Run();

static string? NormalizeUsername(string? username)
{
    var normalized = username?.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return null;
    }

    return normalized.TrimStart('@').ToLowerInvariant();
}

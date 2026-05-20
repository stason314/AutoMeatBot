using AutoMeatBot.Api.Data;
using AutoMeatBot.Api.Domain;
using AutoMeatBot.Api.Dtos;
using AutoMeatBot.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoMeatBot.Api.Services;

public sealed class MeetingAnalysisService(
    AppDbContext db,
    IMeetingExtractor extractor,
    IOptions<MeetingExtractionOptions> options,
    ILogger<MeetingAnalysisService> logger)
{
    public async Task AnalyzeAsync(ChatMessage triggerMessage, CancellationToken cancellationToken)
    {
        var chat = await db.TelegramChats.FirstAsync(chat => chat.Id == triggerMessage.ChatId, cancellationToken);
        var windowSize = Math.Clamp(options.Value.WindowSize, 5, 200);

        var messages = await db.ChatMessages
            .AsNoTracking()
            .Include(message => message.SenderUser)
            .Where(message => message.ChatId == triggerMessage.ChatId)
            .OrderByDescending(message => message.TelegramMessageId)
            .Take(windowSize)
            .OrderBy(message => message.TelegramMessageId)
            .ToListAsync(cancellationToken);

        var extraction = await extractor.ExtractAsync(chat, messages, cancellationToken);
        if (!extraction.HasMeeting || extraction.Confidence < 0.35)
        {
            return;
        }

        await UpsertMeetingAsync(chat, messages, extraction, cancellationToken);
    }

    private async Task UpsertMeetingAsync(
        TelegramChat chat,
        IReadOnlyList<ChatMessage> messages,
        AiMeetingExtraction extraction,
        CancellationToken cancellationToken)
    {
        var activeStatuses = new[]
        {
            MeetingStatus.Draft,
            MeetingStatus.Negotiating,
            MeetingStatus.Proposed,
            MeetingStatus.ConfirmedByAi
        };

        var meeting = await db.MeetingCandidates
            .Include(item => item.Participants)
            .Where(item => item.ChatId == chat.Id && activeStatuses.Contains(item.Status))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        if (meeting is null)
        {
            meeting = new MeetingCandidate
            {
                Id = Guid.NewGuid(),
                ChatId = chat.Id,
                CreatedAtUtc = now
            };
            db.MeetingCandidates.Add(meeting);
        }

        meeting.Status = ParseMeetingStatus(extraction.Status);
        meeting.Topic = FirstNonEmpty(extraction.MeetingTopic, meeting.Topic);
        meeting.ProposedStartUtc = extraction.ProposedDateTime?.UtcDateTime ?? meeting.ProposedStartUtc;
        meeting.TimeZone = FirstNonEmpty(extraction.TimeZone, chat.TimeZone) ?? chat.TimeZone;
        meeting.MeetingUrl = FirstNonEmpty(extraction.MeetingUrl, meeting.MeetingUrl);
        meeting.Confidence = Math.Clamp(extraction.Confidence, 0, 1);
        meeting.AiReason = extraction.Reason;
        meeting.SourceFirstMessageId = messages.FirstOrDefault()?.TelegramMessageId;
        meeting.SourceLastMessageId = messages.LastOrDefault()?.TelegramMessageId;
        meeting.UpdatedAtUtc = now;

        foreach (var participant in extraction.Participants)
        {
            await UpsertParticipantAsync(meeting, participant, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Meeting {MeetingId} updated from chat {ChatId}", meeting.Id, chat.Id);
    }

    private async Task UpsertParticipantAsync(
        MeetingCandidate meeting,
        AiParticipant extracted,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var username = NormalizeUsername(extracted.TelegramUsername);
        var participant = meeting.Participants.FirstOrDefault(item =>
            (extracted.TelegramUserId is not null && item.TelegramUserId == extracted.TelegramUserId) ||
            (!string.IsNullOrWhiteSpace(username) && item.TelegramUsername == username));

        if (participant is null)
        {
            participant = new MeetingParticipant
            {
                Id = Guid.NewGuid(),
                MeetingCandidateId = meeting.Id,
                TelegramUserId = extracted.TelegramUserId,
                TelegramUsername = username,
                CreatedAtUtc = now
            };
            meeting.Participants.Add(participant);
        }

        participant.DisplayName = FirstNonEmpty(extracted.DisplayName, participant.DisplayName);
        participant.Role = string.IsNullOrWhiteSpace(extracted.Role) ? participant.Role : extracted.Role.Trim().ToLowerInvariant();
        participant.Response = ParseParticipantResponse(extracted.Response);
        participant.UpdatedAtUtc = now;

        var mapping = await FindMappingAsync(extracted.TelegramUserId, username, cancellationToken);
        if (mapping is null && (extracted.TelegramUserId is not null || username is not null))
        {
            mapping = new UserEmailMapping
            {
                Id = Guid.NewGuid(),
                TelegramUserId = extracted.TelegramUserId,
                TelegramUsername = username,
                DisplayName = extracted.DisplayName?.Trim(),
                Email = "",
                Source = "auto",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.UserEmailMappings.Add(mapping);
        }

        if (mapping is not null)
        {
            if (string.IsNullOrWhiteSpace(mapping.DisplayName) && !string.IsNullOrWhiteSpace(extracted.DisplayName))
            {
                mapping.DisplayName = extracted.DisplayName.Trim();
            }

            mapping.UpdatedAtUtc = now;
            participant.Email = string.IsNullOrWhiteSpace(mapping.Email) ? participant.Email : mapping.Email;
        }
    }

    private async Task<UserEmailMapping?> FindMappingAsync(long? telegramUserId, string? username, CancellationToken cancellationToken)
    {
        if (telegramUserId is not null)
        {
            var byId = await db.UserEmailMappings.FirstOrDefaultAsync(
                mapping => mapping.TelegramUserId == telegramUserId,
                cancellationToken);

            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return await db.UserEmailMappings.FirstOrDefaultAsync(
                mapping => mapping.TelegramUsername == username,
                cancellationToken);
        }

        return null;
    }

    private static MeetingStatus ParseMeetingStatus(string? status)
    {
        return NormalizeToken(status) switch
        {
            "negotiating" => MeetingStatus.Negotiating,
            "proposed" => MeetingStatus.Proposed,
            "confirmedbyai" => MeetingStatus.ConfirmedByAi,
            "confirmed_ai" => MeetingStatus.ConfirmedByAi,
            "confirmed" => MeetingStatus.ConfirmedByAi,
            "cancelled" => MeetingStatus.Cancelled,
            "canceled" => MeetingStatus.Cancelled,
            _ => MeetingStatus.Draft
        };
    }

    private static ParticipantResponse ParseParticipantResponse(string? response)
    {
        return NormalizeToken(response) switch
        {
            "invited" => ParticipantResponse.Invited,
            "accepted" => ParticipantResponse.Accepted,
            "declined" => ParticipantResponse.Declined,
            "tentative" => ParticipantResponse.Tentative,
            _ => ParticipantResponse.Unknown
        };
    }

    private static string? NormalizeUsername(string? username)
    {
        var normalized = username?.Trim().TrimStart('@').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeToken(string? value)
    {
        return (value ?? "").Trim().Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }
}


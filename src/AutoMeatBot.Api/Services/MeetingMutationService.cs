using AutoMeatBot.Api.Domain;
using AutoMeatBot.Api.Dtos;
using AutoMeatBot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoMeatBot.Api.Services;

public sealed class MeetingMutationService(AppDbContext db)
{
    public async Task<MeetingDto?> UpdateMeetingAsync(Guid id, MeetingUpdateRequest request, CancellationToken cancellationToken)
    {
        var meeting = await LoadMeetingAsync(id, cancellationToken);
        if (meeting is null)
        {
            return null;
        }

        if (request.Topic is not null)
        {
            meeting.Topic = request.Topic.Trim();
        }

        if (request.ProposedStartUtc is not null)
        {
            meeting.ProposedStartUtc = DateTime.SpecifyKind(request.ProposedStartUtc.Value, DateTimeKind.Utc);
        }

        if (request.TimeZone is not null)
        {
            meeting.TimeZone = request.TimeZone.Trim();
        }

        if (request.MeetingUrl is not null)
        {
            meeting.MeetingUrl = string.IsNullOrWhiteSpace(request.MeetingUrl) ? null : request.MeetingUrl.Trim();
        }

        meeting.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(meeting);
    }

    public async Task<MeetingDto?> SetStatusAsync(Guid id, MeetingStatus status, CancellationToken cancellationToken)
    {
        var meeting = await LoadMeetingAsync(id, cancellationToken);
        if (meeting is null)
        {
            return null;
        }

        meeting.Status = status;
        meeting.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(meeting);
    }

    public async Task<MeetingParticipantDto?> AddParticipantAsync(
        Guid meetingId,
        ParticipantCreateRequest request,
        CancellationToken cancellationToken)
    {
        var meeting = await db.MeetingCandidates.FirstOrDefaultAsync(item => item.Id == meetingId, cancellationToken);
        if (meeting is null)
        {
            return null;
        }

        var participant = new MeetingParticipant
        {
            Id = Guid.NewGuid(),
            MeetingCandidateId = meetingId,
            TelegramUserId = request.TelegramUserId,
            TelegramUsername = NormalizeUsername(request.TelegramUsername),
            DisplayName = request.DisplayName?.Trim(),
            Email = request.Email?.Trim(),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "required" : request.Role.Trim().ToLowerInvariant(),
            Response = ParticipantResponse.Invited,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.MeetingParticipants.Add(participant);
        await db.SaveChangesAsync(cancellationToken);

        return new MeetingParticipantDto(
            participant.Id,
            participant.TelegramUserId,
            participant.TelegramUsername,
            participant.DisplayName,
            participant.Email,
            participant.Role,
            participant.Response.ToString());
    }

    private async Task<MeetingCandidate?> LoadMeetingAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.MeetingCandidates
            .Include(meeting => meeting.Chat)
            .Include(meeting => meeting.Participants)
            .ThenInclude(participant => participant.TelegramUser)
            .FirstOrDefaultAsync(meeting => meeting.Id == id, cancellationToken);
    }

    private static MeetingDto ToDto(MeetingCandidate meeting)
    {
        return new MeetingDto(
            meeting.Id,
            meeting.Status.ToString(),
            meeting.Topic,
            meeting.ProposedStartUtc,
            meeting.TimeZone,
            meeting.MeetingUrl,
            meeting.Confidence,
            meeting.AiReason,
            meeting.ChatId,
            meeting.Chat?.Title,
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
                .ToList());
    }

    private static string? NormalizeUsername(string? username)
    {
        var normalized = username?.Trim().TrimStart('@').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

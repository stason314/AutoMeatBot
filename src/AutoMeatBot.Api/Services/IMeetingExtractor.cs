using AutoMeatBot.Api.Domain;
using AutoMeatBot.Api.Dtos;

namespace AutoMeatBot.Api.Services;

public interface IMeetingExtractor
{
    Task<AiMeetingExtraction> ExtractAsync(
        TelegramChat chat,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);
}


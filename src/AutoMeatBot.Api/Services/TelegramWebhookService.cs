using AutoMeatBot.Api.Data;
using AutoMeatBot.Api.Domain;
using AutoMeatBot.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AutoMeatBot.Api.Services;

public sealed class TelegramWebhookService(
    AppDbContext db,
    MeetingAnalysisService meetingAnalysis,
    IOptions<MeetingExtractionOptions> extractionOptions)
{
    public async Task HandleAsync(JsonElement update, CancellationToken cancellationToken)
    {
        if (update.TryGetProperty("business_connection", out var businessConnection))
        {
            await SaveBusinessConnectionAsync(businessConnection, cancellationToken);
            return;
        }

        if (update.TryGetProperty("message", out var message))
        {
            await SaveAndAnalyzeMessageAsync(message, update.GetRawText(), false, cancellationToken);
            return;
        }

        if (update.TryGetProperty("edited_message", out var editedMessage))
        {
            await SaveAndAnalyzeMessageAsync(editedMessage, update.GetRawText(), false, cancellationToken);
            return;
        }

        if (update.TryGetProperty("business_message", out var businessMessage))
        {
            await SaveAndAnalyzeMessageAsync(businessMessage, update.GetRawText(), true, cancellationToken);
            return;
        }

        if (update.TryGetProperty("edited_business_message", out var editedBusinessMessage))
        {
            await SaveAndAnalyzeMessageAsync(editedBusinessMessage, update.GetRawText(), true, cancellationToken);
        }
    }

    private async Task SaveBusinessConnectionAsync(JsonElement connection, CancellationToken cancellationToken)
    {
        var id = GetString(connection, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var record = await db.BusinessConnections.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
        {
            record = new BusinessConnectionRecord
            {
                Id = id,
                CreatedAtUtc = now
            };
            db.BusinessConnections.Add(record);
        }

        record.UserChatId = GetLong(connection, "user_chat_id") ?? 0;
        record.IsEnabled = GetBool(connection, "is_enabled") ?? false;
        record.RightsJson = connection.TryGetProperty("rights", out var rights) ? rights.GetRawText() : "{}";
        record.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveAndAnalyzeMessageAsync(
        JsonElement message,
        string rawUpdateJson,
        bool isBusinessMessage,
        CancellationToken cancellationToken)
    {
        var text = GetString(message, "text") ?? GetString(message, "caption");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!message.TryGetProperty("chat", out var chatElement))
        {
            return;
        }

        var messageId = GetInt(message, "message_id");
        var chatId = GetLong(chatElement, "id");
        if (messageId is null || chatId is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        await UpsertChatAsync(chatElement, now, cancellationToken);

        long? senderId = null;
        if (message.TryGetProperty("from", out var fromElement))
        {
            senderId = await UpsertUserAsync(fromElement, now, cancellationToken);
        }

        var saved = await db.ChatMessages
            .FirstOrDefaultAsync(item => item.ChatId == chatId && item.TelegramMessageId == messageId, cancellationToken);

        if (saved is null)
        {
            saved = new ChatMessage
            {
                Id = Guid.NewGuid(),
                ChatId = chatId.Value,
                TelegramMessageId = messageId.Value,
                CreatedAtUtc = now
            };
            db.ChatMessages.Add(saved);
        }

        saved.SenderUserId = senderId;
        saved.BusinessConnectionId = GetString(message, "business_connection_id");
        saved.IsBusinessMessage = isBusinessMessage;
        saved.Text = text.Trim();
        saved.RawUpdateJson = rawUpdateJson;
        saved.SentAtUtc = DateTimeOffset.FromUnixTimeSeconds(GetLong(message, "date") ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;
        saved.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        await meetingAnalysis.AnalyzeAsync(saved, cancellationToken);
    }

    private async Task UpsertChatAsync(JsonElement chatElement, DateTime now, CancellationToken cancellationToken)
    {
        var chatId = GetLong(chatElement, "id");
        if (chatId is null)
        {
            return;
        }

        var chat = await db.TelegramChats.FirstOrDefaultAsync(item => item.Id == chatId, cancellationToken);
        if (chat is null)
        {
            chat = new TelegramChat
            {
                Id = chatId.Value,
                CreatedAtUtc = now,
                TimeZone = extractionOptions.Value.DefaultTimeZone
            };
            db.TelegramChats.Add(chat);
        }

        chat.Type = GetString(chatElement, "type") ?? chat.Type;
        chat.Title = GetString(chatElement, "title") ?? chat.Title;
        chat.Username = NormalizeUsername(GetString(chatElement, "username")) ?? chat.Username;
        chat.UpdatedAtUtc = now;
    }

    private async Task<long?> UpsertUserAsync(JsonElement userElement, DateTime now, CancellationToken cancellationToken)
    {
        var userId = GetLong(userElement, "id");
        if (userId is null)
        {
            return null;
        }

        var user = await db.TelegramUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null)
        {
            user = new TelegramUser
            {
                Id = userId.Value,
                CreatedAtUtc = now
            };
            db.TelegramUsers.Add(user);
        }

        user.IsBot = GetBool(userElement, "is_bot") ?? false;
        user.Username = NormalizeUsername(GetString(userElement, "username")) ?? user.Username;
        user.FirstName = GetString(userElement, "first_name") ?? user.FirstName;
        user.LastName = GetString(userElement, "last_name") ?? user.LastName;
        user.DisplayName = BuildDisplayName(user);
        user.UpdatedAtUtc = now;
        user.LastSeenAtUtc = now;

        await EnsureAutoMappingAsync(user, now, cancellationToken);
        return user.Id;
    }

    private async Task EnsureAutoMappingAsync(TelegramUser user, DateTime now, CancellationToken cancellationToken)
    {
        UserEmailMapping? mapping;
        if (string.IsNullOrWhiteSpace(user.Username))
        {
            mapping = await db.UserEmailMappings.FirstOrDefaultAsync(
                item => item.TelegramUserId == user.Id,
                cancellationToken);
        }
        else
        {
            mapping = await db.UserEmailMappings.FirstOrDefaultAsync(
                item => item.TelegramUserId == user.Id || item.TelegramUsername == user.Username,
                cancellationToken);
        }

        if (mapping is null)
        {
            db.UserEmailMappings.Add(new UserEmailMapping
            {
                Id = Guid.NewGuid(),
                TelegramUserId = user.Id,
                TelegramUsername = user.Username,
                DisplayName = user.DisplayName,
                Email = "",
                Source = "auto",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            return;
        }

        mapping.TelegramUserId ??= user.Id;
        mapping.TelegramUsername ??= user.Username;
        mapping.DisplayName = string.IsNullOrWhiteSpace(mapping.DisplayName) ? user.DisplayName : mapping.DisplayName;
        mapping.UpdatedAtUtc = now;
    }

    private static string BuildDisplayName(TelegramUser user)
    {
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        return user.Username ?? user.Id.ToString();
    }

    private static string? NormalizeUsername(string? username)
    {
        var normalized = username?.Trim().TrimStart('@').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }
}

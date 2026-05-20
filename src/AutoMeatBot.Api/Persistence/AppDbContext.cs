using AutoMeatBot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AutoMeatBot.Api.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TelegramChat> TelegramChats => Set<TelegramChat>();
    public DbSet<TelegramUser> TelegramUsers => Set<TelegramUser>();
    public DbSet<UserEmailMapping> UserEmailMappings => Set<UserEmailMapping>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<MeetingCandidate> MeetingCandidates => Set<MeetingCandidate>();
    public DbSet<MeetingParticipant> MeetingParticipants => Set<MeetingParticipant>();
    public DbSet<BusinessConnectionRecord> BusinessConnections => Set<BusinessConnectionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelegramChat>(entity =>
        {
            entity.HasKey(chat => chat.Id);
            entity.Property(chat => chat.Type).HasMaxLength(64);
            entity.Property(chat => chat.Title).HasMaxLength(512);
            entity.Property(chat => chat.Username).HasMaxLength(128);
            entity.Property(chat => chat.TimeZone).HasMaxLength(128);
        });

        modelBuilder.Entity<TelegramUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Username).HasMaxLength(128);
            entity.Property(user => user.FirstName).HasMaxLength(256);
            entity.Property(user => user.LastName).HasMaxLength(256);
            entity.Property(user => user.DisplayName).HasMaxLength(512);
            entity.HasIndex(user => user.Username);
        });

        modelBuilder.Entity<UserEmailMapping>(entity =>
        {
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.TelegramUsername).HasMaxLength(128);
            entity.Property(mapping => mapping.DisplayName).HasMaxLength(512);
            entity.Property(mapping => mapping.Email).HasMaxLength(320);
            entity.Property(mapping => mapping.Source).HasMaxLength(64);
            entity.HasIndex(mapping => mapping.TelegramUserId);
            entity.HasIndex(mapping => mapping.TelegramUsername);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Text).HasMaxLength(16000);
            entity.Property(message => message.RawUpdateJson).HasColumnType("jsonb");
            entity.Property(message => message.BusinessConnectionId).HasMaxLength(256);
            entity.HasIndex(message => new { message.ChatId, message.TelegramMessageId }).IsUnique();
            entity.HasOne(message => message.Chat)
                .WithMany()
                .HasForeignKey(message => message.ChatId);
            entity.HasOne(message => message.SenderUser)
                .WithMany()
                .HasForeignKey(message => message.SenderUserId);
        });

        modelBuilder.Entity<MeetingCandidate>(entity =>
        {
            entity.HasKey(meeting => meeting.Id);
            entity.Property(meeting => meeting.Status).HasConversion<string>().HasMaxLength(64);
            entity.Property(meeting => meeting.Topic).HasMaxLength(1024);
            entity.Property(meeting => meeting.TimeZone).HasMaxLength(128);
            entity.Property(meeting => meeting.MeetingUrl).HasMaxLength(2048);
            entity.Property(meeting => meeting.AiReason).HasMaxLength(4000);
            entity.HasIndex(meeting => new { meeting.ChatId, meeting.Status });
            entity.HasOne(meeting => meeting.Chat)
                .WithMany()
                .HasForeignKey(meeting => meeting.ChatId);
        });

        modelBuilder.Entity<MeetingParticipant>(entity =>
        {
            entity.HasKey(participant => participant.Id);
            entity.Property(participant => participant.TelegramUsername).HasMaxLength(128);
            entity.Property(participant => participant.DisplayName).HasMaxLength(512);
            entity.Property(participant => participant.Email).HasMaxLength(320);
            entity.Property(participant => participant.Role).HasMaxLength(64);
            entity.Property(participant => participant.Response).HasConversion<string>().HasMaxLength(64);
            entity.HasIndex(participant => new { participant.MeetingCandidateId, participant.TelegramUserId });
            entity.HasIndex(participant => new { participant.MeetingCandidateId, participant.TelegramUsername });
        });

        modelBuilder.Entity<BusinessConnectionRecord>(entity =>
        {
            entity.HasKey(connection => connection.Id);
            entity.Property(connection => connection.Id).HasMaxLength(256);
            entity.Property(connection => connection.RightsJson).HasColumnType("jsonb");
        });
    }
}

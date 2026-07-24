using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class NotificationOutboxMessageHandler : IOutboxMessageHandler
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
        private readonly IAppDbContext _context;
        private readonly INotificationSender _sender;
        private readonly ILogger<NotificationOutboxMessageHandler> _logger;
        private readonly ISensitivePayloadProtector? _sensitivePayloadProtector;

        public NotificationOutboxMessageHandler(
            IAppDbContext context,
            INotificationSender sender,
            ILogger<NotificationOutboxMessageHandler> logger)
            : this(context, sender, logger, null)
        {
        }

        public NotificationOutboxMessageHandler(
            IAppDbContext context,
            INotificationSender sender,
            ILogger<NotificationOutboxMessageHandler> logger,
            ISensitivePayloadProtector? sensitivePayloadProtector)
        {
            _context = context;
            _sender = sender;
            _logger = logger;
            _sensitivePayloadProtector = sensitivePayloadProtector;
        }

        public async Task HandleAsync(
            OutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            var serializedPayload = message.Type switch
            {
                OutboxMessageTypes.NotificationRequested => message.Payload,
                OutboxMessageTypes.ProtectedNotificationRequested =>
                    (_sensitivePayloadProtector
                        ?? throw new InvalidOperationException(
                            "Sensitive outbox payload protection is not configured."))
                    .Unprotect(message.Payload),
                _ => throw new InvalidOperationException(
                    $"Loại thông báo trong hàng đợi '{message.Type}' không được hỗ trợ.")
            };

            var payload = JsonSerializer.Deserialize<NotificationRequestedPayload>(
                serializedPayload,
                SerializerOptions)
                ?? throw new InvalidOperationException("Nội dung thông báo trong hàng đợi không hợp lệ.");
            var recipientEmail = await _context.Users
                .AsNoTracking()
                .Where(user => user.Id == payload.UserId && !user.IsDeleted)
                .Select(user => user.Email)
                .SingleOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                _logger.LogWarning(
                    "Skipping outbox notification {OutboxMessageId}: recipient user {UserId} is unavailable.",
                    message.Id,
                    payload.UserId);
                return;
            }

            await _sender.SendAsync(
                recipientEmail,
                payload.Subject,
                payload.Message,
                message.Id,
                cancellationToken);
        }
    }
}

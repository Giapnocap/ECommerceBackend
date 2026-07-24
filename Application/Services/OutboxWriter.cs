using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public sealed class OutboxWriter : IOutboxWriter
    {
        private const int MaxSubjectLength = 200;
        private const int MaxMessageLength = 4000;
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
        private readonly IAppDbContext _context;
        private readonly TimeProvider _timeProvider;

        public OutboxWriter(IAppDbContext context)
            : this(context, TimeProvider.System)
        {
        }

        public OutboxWriter(IAppDbContext context, TimeProvider timeProvider)
        {
            _context = context;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public void EnqueueNotification(
            Guid userId,
            string subject,
            string message,
            Guid? orderId = null,
            Guid? paymentId = null)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("ID người nhận thông báo là bắt buộc.", nameof(userId));
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength)
                throw new ArgumentException($"Tiêu đề thông báo phải có từ 1 đến {MaxSubjectLength} ký tự.", nameof(subject));
            if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
                throw new ArgumentException($"Nội dung thông báo phải có từ 1 đến {MaxMessageLength} ký tự.", nameof(message));

            var now = UtcNow;
            var payload = new NotificationRequestedPayload(
                userId,
                subject,
                message,
                orderId,
                paymentId);

            _context.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = OutboxMessageTypes.NotificationRequested,
                Payload = JsonSerializer.Serialize(payload, SerializerOptions),
                OccurredAt = now,
                NextAttemptAt = now
            });
        }
    }
}

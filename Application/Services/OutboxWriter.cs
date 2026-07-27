using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public sealed class OutboxWriter : IOutboxWriter
    {
        private const int MaxSubjectLength = 200;
        private const int MaxMessageLength = 4000;
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
        private readonly IOutboxRepository _outboxRepository;
        private readonly TimeProvider _timeProvider;
        private readonly ISensitivePayloadProtector? _sensitivePayloadProtector;

        public OutboxWriter(IOutboxRepository outboxRepository)
            : this(outboxRepository, TimeProvider.System, null)
        {
        }

        public OutboxWriter(
            IOutboxRepository outboxRepository,
            TimeProvider timeProvider)
            : this(outboxRepository, timeProvider, null)
        {
        }

        public OutboxWriter(
            IOutboxRepository outboxRepository,
            TimeProvider timeProvider,
            ISensitivePayloadProtector? sensitivePayloadProtector)
        {
            _outboxRepository = outboxRepository;
            _timeProvider = timeProvider;
            _sensitivePayloadProtector = sensitivePayloadProtector;
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

            _outboxRepository.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = OutboxMessageTypes.NotificationRequested,
                Payload = JsonSerializer.Serialize(payload, SerializerOptions),
                OccurredAt = now,
                NextAttemptAt = now
            });
        }

        public void EnqueueSensitiveNotification(
            Guid userId,
            string subject,
            string message)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("ID người nhận thông báo là bắt buộc.", nameof(userId));
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength)
                throw new ArgumentException(
                    $"Tiêu đề thông báo phải có từ 1 đến {MaxSubjectLength} ký tự.",
                    nameof(subject));
            if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
                throw new ArgumentException(
                    $"Nội dung thông báo phải có từ 1 đến {MaxMessageLength} ký tự.",
                    nameof(message));

            var protector = _sensitivePayloadProtector
                ?? throw new InvalidOperationException(
                    "Sensitive outbox payload protection is not configured.");
            var now = UtcNow;
            var payload = JsonSerializer.Serialize(
                new NotificationRequestedPayload(userId, subject, message),
                SerializerOptions);
            _outboxRepository.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = OutboxMessageTypes.ProtectedNotificationRequested,
                Payload = protector.Protect(payload),
                OccurredAt = now,
                NextAttemptAt = now
            });
        }
    }
}

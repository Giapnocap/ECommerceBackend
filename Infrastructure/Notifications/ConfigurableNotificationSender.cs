using System.Net;
using System.Net.Mail;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Notifications
{
    public sealed class ConfigurableNotificationSender : INotificationSender
    {
        private const string MessageIdDomain =
            "notifications.ecommercebackend.local";
        private readonly SmtpOptions _options;
        private readonly ILogger<ConfigurableNotificationSender> _logger;

        public ConfigurableNotificationSender(
            IOptions<SmtpOptions> options,
            ILogger<ConfigurableNotificationSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendAsync(
            string recipientEmail,
            string subject,
            string message,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation(
                    "Notification {NotificationId} recorded without SMTP delivery.",
                    idempotencyKey);
                return;
            }

            using var mail = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject,
                Body = message,
                IsBodyHtml = false
            };
            mail.To.Add(new MailAddress(recipientEmail));
            mail.Headers.Add(
                "Message-ID",
                $"<{idempotencyKey:N}@{MessageIdDomain}>");
            mail.Headers.Add("X-Idempotency-Key", idempotencyKey.ToString("N"));

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                Timeout = checked(_options.TimeoutSeconds * 1000)
            };
            if (!string.IsNullOrWhiteSpace(_options.UserName))
                client.Credentials = new NetworkCredential(_options.UserName, _options.Password);

            await client.SendMailAsync(mail, cancellationToken);
        }
    }
}

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ECommerceBackend.Infrastructure.Observability
{
    public sealed class DatabaseTelemetryInterceptor : DbCommandInterceptor
    {
        public const string MeterName = "ECommerceBackend.Database";

        private static readonly Meter Meter = new(MeterName);
        private static readonly Histogram<double> CommandDuration =
            Meter.CreateHistogram<double>("database.command.duration", "ms");
        private static readonly Counter<long> CommandCounter =
            Meter.CreateCounter<long>("database.commands");

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            Record(command, eventData.Duration, "success");
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Record(command, eventData.Duration, "success");
            return ValueTask.FromResult(result);
        }

        public override int NonQueryExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result)
        {
            Record(command, eventData.Duration, "success");
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            Record(command, eventData.Duration, "success");
            return ValueTask.FromResult(result);
        }

        public override object? ScalarExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result)
        {
            Record(command, eventData.Duration, "success");
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            object? result,
            CancellationToken cancellationToken = default)
        {
            Record(command, eventData.Duration, "success");
            return ValueTask.FromResult(result);
        }

        public override void CommandFailed(
            DbCommand command,
            CommandErrorEventData eventData)
            => Record(
                command,
                eventData.Duration,
                eventData.Exception is OperationCanceledException ? "cancelled" : "error");

        public override Task CommandFailedAsync(
            DbCommand command,
            CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Record(
                command,
                eventData.Duration,
                eventData.Exception is OperationCanceledException ? "cancelled" : "error");
            return Task.CompletedTask;
        }

        private static void Record(DbCommand command, TimeSpan duration, string outcome)
        {
            var tags = new TagList
            {
                { "db.system.name", "microsoft.sql_server" },
                { "db.operation.name", GetOperationName(command) },
                { "db.operation.outcome", outcome }
            };
            CommandCounter.Add(1, tags);
            CommandDuration.Record(duration.TotalMilliseconds, tags);
        }

        private static string GetOperationName(DbCommand command)
        {
            if (command.CommandType == CommandType.StoredProcedure)
                return "CALL";

            var text = command.CommandText.AsSpan().TrimStart();
            var length = 0;
            while (length < text.Length && char.IsAsciiLetter(text[length]))
                length++;

            var operation = text[..length].ToString().ToUpperInvariant();
            return operation is "SELECT" or "INSERT" or "UPDATE" or "DELETE" or "MERGE"
                ? operation
                : "OTHER";
        }
    }
}

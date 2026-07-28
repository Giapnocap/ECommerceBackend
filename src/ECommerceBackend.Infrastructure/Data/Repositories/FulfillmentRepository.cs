using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class FulfillmentRepository : IFulfillmentRepository
    {
        private readonly AppDbContext _context;

        public FulfillmentRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<Shipment?> LockShipmentByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return _context.Shipments
                    .FromSqlInterpolated(
                        $"SELECT * FROM [Shipments] WITH (UPDLOCK, ROWLOCK) WHERE [OrderId] = {orderId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return _context.Shipments.SingleOrDefaultAsync(
                shipment => shipment.OrderId == orderId,
                cancellationToken);
        }

        public Task<ReturnRequest?> LockReturnRequestByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return _context.ReturnRequests
                    .FromSqlInterpolated(
                        $"SELECT * FROM [ReturnRequests] WITH (UPDLOCK, ROWLOCK) WHERE [OrderId] = {orderId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return _context.ReturnRequests.SingleOrDefaultAsync(
                returnRequest => returnRequest.OrderId == orderId,
                cancellationToken);
        }

        public void AddShipment(Shipment shipment)
            => _context.Shipments.Add(shipment);

        public void AddReturnRequest(ReturnRequest returnRequest)
            => _context.ReturnRequests.Add(returnRequest);
    }
}

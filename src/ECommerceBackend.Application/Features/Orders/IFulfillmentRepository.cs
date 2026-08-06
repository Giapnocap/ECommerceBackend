using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IFulfillmentRepository
    {
        Task<Shipment?> LockShipmentByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default);

        Task<ReturnRequest?> LockReturnRequestByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default);

        void AddShipment(Shipment shipment);

        void AddReturnRequest(ReturnRequest returnRequest);
    }
}

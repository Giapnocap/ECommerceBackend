namespace ECommerceBackend.Domain.Enums
{
    public enum InventoryTransactionType
    {
        InitialStock = 0,
        ManualAdjustment = 1,
        OrderPlaced = 2,
        OrderCancelled = 3,
        OrderReturned = 4,
        StockIn = 5
    }
}

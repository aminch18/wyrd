using System.Text.Json.Serialization;
using Shop.Contracts;
using Shop.Inventory;
using Shop.Orders;
using Shop.Payments;
using Shop.Shipping;
using Wyrd;

namespace Shop;

/// <summary>
/// Source-generated JSON for everything Wyrd stores and sends: no reflection, Native AOT ready.
/// Wyrd's analyzers keep it complete (WYRD0300) and offer a code fix for every missing type.
/// </summary>
[WyrdJsonContext]
// Orders
[JsonSerializable(typeof(AddLine))]
[JsonSerializable(typeof(LineAdded))]
[JsonSerializable(typeof(CancelLine))]
[JsonSerializable(typeof(LineCancelled))]
[JsonSerializable(typeof(SubmitOrder))]
[JsonSerializable(typeof(CancelDraftOrder))]
[JsonSerializable(typeof(OrderDetail))]
[JsonSerializable(typeof(PlaceOrder))]
[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(CancelOrder))]
[JsonSerializable(typeof(OrderCancelled))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(Order.None), TypeInfoPropertyName = "OrderNone")] // two unions have a 'None' case
[JsonSerializable(typeof(OrderError))]
[JsonSerializable(typeof(OrderSummary))]
// Inventory
[JsonSerializable(typeof(Restock))]
[JsonSerializable(typeof(ReserveStock))]
[JsonSerializable(typeof(ReleaseStock))]
[JsonSerializable(typeof(ConsumeStock))]
[JsonSerializable(typeof(StockAdded))]
[JsonSerializable(typeof(StockReserved))]
[JsonSerializable(typeof(StockReleased))]
[JsonSerializable(typeof(StockConsumed))]
[JsonSerializable(typeof(StockItem))]
[JsonSerializable(typeof(InventoryError))]
// Payments
[JsonSerializable(typeof(ChargePayment))]
[JsonSerializable(typeof(RefundPayment))]
[JsonSerializable(typeof(PaymentCharged))]
[JsonSerializable(typeof(PaymentRefunded))]
[JsonSerializable(typeof(Payment))]
[JsonSerializable(typeof(Payment.None), TypeInfoPropertyName = "PaymentNone")]
[JsonSerializable(typeof(PaymentError))]
// Shipping
[JsonSerializable(typeof(ShipOrder))]
[JsonSerializable(typeof(OrderShipped))]
[JsonSerializable(typeof(Shipment))]
[JsonSerializable(typeof(ShippingError))]
// Contracts
[JsonSerializable(typeof(PaymentConfirmed))]
[JsonSerializable(typeof(PaymentChargedV1))]
[JsonSerializable(typeof(OrderShippedV1))]
public partial class ShopJson : JsonSerializerContext;

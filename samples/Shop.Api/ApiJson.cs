using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Shop.Orders;

namespace Shop.Api;

// Only HTTP payload roots belong here; ShopJson remains the source for domain metadata.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RestockRequest))]
[JsonSerializable(typeof(PlaceOrderRequest))]
[JsonSerializable(typeof(AddLineRequest))]
[JsonSerializable(typeof(PaymentWebhook))]
[JsonSerializable(typeof(OrderResponse))]
[JsonSerializable(typeof(StockResponse))]
[JsonSerializable(typeof(ClockResponse))]
[JsonSerializable(typeof(IReadOnlyList<OrderSummary>))]
[JsonSerializable(typeof(IReadOnlyList<OrderDetail>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class ApiJson : JsonSerializerContext;

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SportsStore.Models;
using SportsStore.Services;

namespace SportsStore.Controllers {

    public class OrderController : Controller {
        private const string PendingOrderKey = "PendingOrder";

        private readonly IOrderRepository _repository;
        private readonly Cart _cart;
        private readonly IStripePaymentService _stripePayment;
        private readonly ILogger<OrderController> _logger;

        public OrderController(
            IOrderRepository repoService,
            Cart cartService,
            IStripePaymentService stripePayment,
            ILogger<OrderController> logger)
        {
            _repository = repoService;
            _cart = cartService;
            _stripePayment = stripePayment;
            _logger = logger;
        }

        public ViewResult Checkout()
        {
            var userName = User?.Identity?.Name ?? "anonymous";
            _logger.LogInformation(
                "Checkout page displayed. Cart line count: {LineCount}, SessionId: {SessionId}, UserName: {UserName}",
                _cart.Lines.Count(),
                HttpContext.Session.Id,
                userName);
            return View(new Order());
        }

        [HttpPost]
        public async Task<IActionResult> Checkout(Order order, CancellationToken cancellationToken)
        {
            if (_cart.Lines.Count() == 0)
            {
                _logger.LogWarning(
                    "Checkout attempted with empty cart. SessionId: {SessionId}, UserName: {UserName}",
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                ModelState.AddModelError("", "Sorry, your cart is empty!");
            }

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                _logger.LogWarning(
                    "Checkout validation failed. Errors: {Errors}, SessionId: {SessionId}, UserName: {UserName}",
                    errors,
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                return View(order);
            }

            decimal total = _cart.ComputeTotalValue();
            if (total <= 0)
            {
                _logger.LogWarning(
                    "Checkout attempted with non-positive total: {Total}, SessionId: {SessionId}, UserName: {UserName}",
                    total,
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                ModelState.AddModelError("", "Invalid cart total.");
                return View(order);
            }

            var pending = new PendingOrderDto
            {
                Name = order.Name,
                Line1 = order.Line1,
                Line2 = order.Line2,
                Line3 = order.Line3,
                City = order.City,
                State = order.State,
                Zip = order.Zip,
                Country = order.Country,
                GiftWrap = order.GiftWrap
            };
            HttpContext.Session.SetString(PendingOrderKey, JsonSerializer.Serialize(pending));

            long amountCents = (long)(total * 100);
            var productIds = _cart.Lines.Select(l => l.Product.ProductID).ToArray();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var successUrl = $"{baseUrl}/Order/PaymentSuccess?session_id={{CHECKOUT_SESSION_ID}}";
            var cancelUrl = $"{baseUrl}/Order/PaymentCancel";

            _logger.LogInformation(
                "Creating Stripe checkout session. AmountCents: {AmountCents}, CartTotal: {CartTotal}, ProductIds: {ProductIds}, SessionId: {SessionId}, UserName: {UserName}",
                amountCents,
                total,
                productIds,
                HttpContext.Session.Id,
                User?.Identity?.Name ?? "anonymous");

            string? checkoutUrl = await _stripePayment.CreateCheckoutSessionAsync(
                amountCents, successUrl, cancelUrl, cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(checkoutUrl))
            {
                _logger.LogError(
                    "Stripe checkout session creation failed. Check Stripe:SecretKey configuration. SessionId: {SessionId}, UserName: {UserName}",
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                HttpContext.Session.Remove(PendingOrderKey);
                ModelState.AddModelError("", "Payment service is not configured. Please set Stripe keys.");
                return View(order);
            }

            return Redirect(checkoutUrl);
        }

        [HttpGet]
        public async Task<IActionResult> PaymentSuccess(string? session_id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(session_id))
            {
                _logger.LogWarning(
                    "PaymentSuccess called without session_id. SessionId: {SessionId}, UserName: {UserName}",
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                TempData["PaymentError"] = "Missing payment session. Please try checkout again.";
                return RedirectToAction(nameof(Checkout));
            }

            bool isPaid = await _stripePayment.IsSessionPaidAsync(session_id, cancellationToken);
            if (!isPaid)
            {
                _logger.LogWarning(
                    "PaymentSuccess called but session is not paid. StripeSessionId: {StripeSessionId}, SessionId: {SessionId}, UserName: {UserName}",
                    session_id,
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                TempData["PaymentError"] = "Payment was not completed.";
                return RedirectToAction(nameof(Checkout));
            }

            string? pendingJson = HttpContext.Session.GetString(PendingOrderKey);
            if (string.IsNullOrEmpty(pendingJson))
            {
                _logger.LogWarning(
                    "PaymentSuccess: no pending order in session. StripeSessionId: {StripeSessionId}, SessionId: {SessionId}, UserName: {UserName}",
                    session_id,
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                TempData["PaymentError"] = "Session expired. Please try checkout again.";
                return RedirectToAction(nameof(Checkout));
            }

            var pending = JsonSerializer.Deserialize<PendingOrderDto>(pendingJson);
            if (pending == null)
            {
                _logger.LogWarning(
                    "PaymentSuccess: failed to deserialize pending order. StripeSessionId: {StripeSessionId}, SessionId: {SessionId}, UserName: {UserName}",
                    session_id,
                    HttpContext.Session.Id,
                    User?.Identity?.Name ?? "anonymous");
                TempData["PaymentError"] = "Could not restore your order. Please try again.";
                return RedirectToAction(nameof(Checkout));
            }

            var order = new Order
            {
                Name = pending.Name,
                Line1 = pending.Line1,
                Line2 = pending.Line2,
                Line3 = pending.Line3,
                City = pending.City,
                State = pending.State,
                Zip = pending.Zip,
                Country = pending.Country,
                GiftWrap = pending.GiftWrap,
                Lines = _cart.Lines.ToArray(),
                StripeSessionId = session_id
            };

            _repository.SaveOrder(order);
            _cart.Clear();
            HttpContext.Session.Remove(PendingOrderKey);

            var productIds = order.Lines.Select(l => l.Product.ProductID).ToArray();
            _logger.LogInformation(
                "Order created after successful payment. OrderId: {OrderId}, StripeSessionId: {StripeSessionId}, ProductIds: {ProductIds}, SessionId: {SessionId}, UserName: {UserName}",
                order.OrderID,
                session_id,
                productIds,
                HttpContext.Session.Id,
                User?.Identity?.Name ?? "anonymous");

            return RedirectToPage("/Completed", new { orderId = order.OrderID });
        }

        [HttpGet]
        public IActionResult PaymentCancel()
        {
            HttpContext.Session.Remove(PendingOrderKey);
            _logger.LogInformation(
                "Payment cancelled by user. SessionId: {SessionId}, UserName: {UserName}",
                HttpContext.Session.Id,
                User?.Identity?.Name ?? "anonymous");
            TempData["PaymentMessage"] = "Payment was cancelled. You can try again from checkout.";
            return RedirectToAction(nameof(Checkout));
        }
    }
}

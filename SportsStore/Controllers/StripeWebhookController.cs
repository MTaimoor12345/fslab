using System.Text;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;

namespace SportsStore.Controllers;

/// <summary>
/// Receives Stripe webhooks (e.g. card declined) and logs them so they appear in Seq.
/// </summary>
[Route("webhooks")]
[ApiController]
[IgnoreAntiforgeryToken]
public class StripeWebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(IConfiguration configuration, ILogger<StripeWebhookController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stripe webhook POST received at /webhooks/stripe.");

        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogWarning("Stripe webhook received but Stripe:WebhookSecret is not configured. Skipping verification.");
            return BadRequest();
        }

        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Stripe webhook received without Stripe-Signature header.");
            return BadRequest();
        }

        Event? stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(body, signature, webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case "payment_intent.payment_failed":
                var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                if (paymentIntent != null)
                {
                    var errorMessage = paymentIntent.LastPaymentError?.Message ?? "Unknown error";
                    var errorCode = paymentIntent.LastPaymentError?.Code ?? "unknown";
                    _logger.LogWarning(
                        "Stripe card declined / payment failed. PaymentIntentId: {PaymentIntentId}, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}, StripeEventId: {StripeEventId}",
                        paymentIntent.Id,
                        errorCode,
                        errorMessage,
                        stripeEvent.Id);
                }
                break;

            case "charge.failed":
                var charge = stripeEvent.Data.Object as Charge;
                if (charge != null)
                {
                    var failMsg = charge.FailureMessage ?? charge.FailureCode ?? "Card declined or charge failed";
                    _logger.LogWarning(
                        "Stripe charge failed (e.g. card declined). ChargeId: {ChargeId}, FailureCode: {FailureCode}, FailureMessage: {FailureMessage}, StripeEventId: {StripeEventId}",
                        charge.Id,
                        charge.FailureCode ?? "unknown",
                        failMsg,
                        stripeEvent.Id);
                }
                break;

            case "checkout.session.expired":
                var sessionExpired = stripeEvent.Data.Object as Session;
                if (sessionExpired != null)
                {
                    _logger.LogInformation(
                        "Stripe checkout session expired. SessionId: {StripeSessionId}, StripeEventId: {StripeEventId}",
                        sessionExpired.Id,
                        stripeEvent.Id);
                }
                break;

            default:
                _logger.LogInformation("Stripe webhook event received (not payment_failed). Type: {EventType}, Id: {StripeEventId}", stripeEvent.Type, stripeEvent.Id);
                break;
        }

        return Ok();
    }
}

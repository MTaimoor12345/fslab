using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace SportsStore.Services;

/// <summary>
/// Stripe payment service using official SDK. Uses test keys only (from configuration).
/// </summary>
public class StripePaymentService : IStripePaymentService
{
    private readonly string? _secretKey;

    public StripePaymentService(IConfiguration configuration)
    {
        _secretKey = configuration["Stripe:SecretKey"];
    }

    private bool EnsureApiKey()
    {
        if (string.IsNullOrWhiteSpace(_secretKey))
        {
            return false;
        }

        StripeConfiguration.ApiKey = _secretKey;
        return true;
    }

    public async Task<string?> CreateCheckoutSessionAsync(
        long amountCents,
        string successUrl,
        string cancelUrl,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureApiKey())
        {
            return null;
        }

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = amountCents,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Sports Store Order"
                        }
                    },
                    Quantity = 1
                }
            }
        };

        if (metadata != null && metadata.Count > 0)
        {
            options.Metadata = new Dictionary<string, string>(metadata);
        }

        var service = new SessionService();
        Session session = await service.CreateAsync(options, cancellationToken: cancellationToken);
        return session.Url;
    }

    public async Task<bool> IsSessionPaidAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureApiKey())
        {
            return false;
        }

        var service = new SessionService();
        Session session = await service.GetAsync(sessionId, cancellationToken: cancellationToken);
        return session.PaymentStatus == "paid";
    }
}


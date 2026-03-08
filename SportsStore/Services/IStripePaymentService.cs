using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SportsStore.Services;

/// <summary>Abstraction for Stripe payment operations (test keys only).</summary>
public interface IStripePaymentService
{
    /// <summary>
    /// Creates a Stripe Checkout Session and returns the hosted checkout URL,
    /// or null if the service is not configured.
    /// </summary>
    Task<string?> CreateCheckoutSessionAsync(
        long amountCents,
        string successUrl,
        string cancelUrl,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the given Checkout Session has been fully paid.
    /// </summary>
    Task<bool> IsSessionPaidAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}


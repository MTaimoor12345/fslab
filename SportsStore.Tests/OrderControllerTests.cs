using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SportsStore.Controllers;
using SportsStore.Models;
using SportsStore.Services;
using Xunit;

namespace SportsStore.Tests {

    public class OrderControllerTests {

        private static ILogger<OrderController> CreateLogger() =>
            new Mock<ILogger<OrderController>>().Object;

        private static IStripePaymentService CreateStripe(bool returnUrl, string? url = null)
        {
            var mock = new Mock<IStripePaymentService>();
            if (returnUrl)
            {
                mock.Setup(s => s.CreateCheckoutSessionAsync(
                        It.IsAny<long>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<IReadOnlyDictionary<string, string>>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(url ?? "https://checkout.stripe.com/test-session");
            }
            return mock.Object;
        }

        [Fact]
        public async Task Cannot_Checkout_Empty_Cart() {
            var mock = new Mock<IOrderRepository>();
            Cart cart = new Cart();
            Order order = new Order();
            var stripe = CreateStripe(returnUrl: false);
            OrderController target = new OrderController(mock.Object, cart, stripe, CreateLogger());
            var ctx = new DefaultHttpContext();
            ctx.Session = new TestSession();
            target.ControllerContext = new ControllerContext { HttpContext = ctx };

            ViewResult? result = (await target.Checkout(order, default)) as ViewResult;

            mock.Verify(m => m.SaveOrder(It.IsAny<Order>()), Times.Never);
            Assert.True(string.IsNullOrEmpty(result?.ViewName));
            Assert.False(result?.ViewData.ModelState.IsValid);
        }

        [Fact]
        public async Task Cannot_Checkout_Invalid_ShippingDetails() {
            var mock = new Mock<IOrderRepository>();
            Cart cart = new Cart();
            cart.AddItem(new Product { Price = 10 }, 1);
            var stripe = CreateStripe(returnUrl: false);
            OrderController target = new OrderController(mock.Object, cart, stripe, CreateLogger());
            target.ModelState.AddModelError("error", "error");
            var ctx = new DefaultHttpContext();
            ctx.Session = new TestSession();
            target.ControllerContext = new ControllerContext { HttpContext = ctx };

            ViewResult? result = (await target.Checkout(new Order(), default)) as ViewResult;

            mock.Verify(m => m.SaveOrder(It.IsAny<Order>()), Times.Never);
            Assert.True(string.IsNullOrEmpty(result?.ViewName));
            Assert.False(result?.ViewData.ModelState.IsValid);
        }

        [Fact]
        public async Task Checkout_Redirects_To_Stripe_When_Valid() {
            var mock = new Mock<IOrderRepository>();
            Cart cart = new Cart();
            cart.AddItem(new Product { Price = 10 }, 1);
            var stripeMock = new Mock<IStripePaymentService>();
            stripeMock
                .Setup(s => s.CreateCheckoutSessionAsync(
                    It.IsAny<long>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://checkout.stripe.com/test-session");

            OrderController target = new OrderController(mock.Object, cart, stripeMock.Object, CreateLogger());
            var ctx = new DefaultHttpContext();
            ctx.Session = new TestSession();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("localhost", 5001);
            target.ControllerContext = new ControllerContext { HttpContext = ctx };

            var order = new Order { Name = "Test", Line1 = "L1", City = "C", State = "S", Country = "CO" };
            RedirectResult? result = (await target.Checkout(order, default)) as RedirectResult;

            mock.Verify(m => m.SaveOrder(It.IsAny<Order>()), Times.Never);
            Assert.NotNull(result);
            Assert.StartsWith("https://checkout.stripe.com", result!.Url);
        }
    }

    internal class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public string Id => "test";
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => _store.Keys;

        public void Clear() => _store.Clear();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public void Set(string key, byte[] value) => _store[key] = value;

        public bool TryGetValue(string key, out byte[]? value)
        {
            bool ok = _store.TryGetValue(key, out var v);
            value = v;
            return ok;
        }
    }
}

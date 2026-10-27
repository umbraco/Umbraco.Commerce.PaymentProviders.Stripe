using FluentAssertions;
using Microsoft.Extensions.Logging;
using Stripe;
using Umbraco.Commerce.Core.Models;

namespace Umbraco.Commerce.PaymentProviders.Stripe.UnitTests
{
    public class StripePaymentStatusTests
    {
        private readonly TestProvider _provider = new();

        [Theory]
        [InlineData(true, true, false, 0, PaymentStatus.Captured)]
        [InlineData(true, true, false, 2500, PaymentStatus.PartiallyRefunded)]
        [InlineData(true, true, false, 9999, PaymentStatus.PartiallyRefunded)]
        [InlineData(true, true, true, 10000, PaymentStatus.Refunded)]
        [InlineData(true, false, false, 0, PaymentStatus.Authorized)]
        [InlineData(true, false, true, 10000, PaymentStatus.Cancelled)]
        [InlineData(false, false, false, 0, PaymentStatus.Initialized)]
        public void GetPaymentStatus_Charge_Should_Map_Captured_And_Refunded_Amounts(bool paid, bool captured, bool refunded, long amountRefunded, PaymentStatus expected)
        {
            // preparation
            Charge charge = new()
            {
                Paid = paid,
                Captured = captured,
                Refunded = refunded,
                Amount = 10000,
                AmountRefunded = amountRefunded,
            };

            // execution
            PaymentStatus status = _provider.MapStatus(charge);

            // asserts
            status.Should().Be(expected);
        }

        [Fact]
        public void GetPaymentStatus_Charge_Should_Return_Initialized_When_Null()
        {
            _provider.MapStatus((Charge)null!).Should().Be(PaymentStatus.Initialized);
        }

        [Theory]
        [InlineData(0, false, PaymentStatus.Captured)]
        [InlineData(2500, false, PaymentStatus.PartiallyRefunded)]
        [InlineData(10000, true, PaymentStatus.Refunded)]
        public void GetPaymentStatus_PaymentIntent_Should_Use_LatestCharge_Refund_State(long amountRefunded, bool refunded, PaymentStatus expected)
        {
            // preparation
            PaymentIntent intent = new()
            {
                Status = "succeeded",
                LatestCharge = new Charge
                {
                    Paid = true,
                    Captured = true,
                    Refunded = refunded,
                    Amount = 10000,
                    AmountRefunded = amountRefunded,
                },
            };

            // execution
            PaymentStatus status = _provider.MapStatus(intent);

            // asserts
            status.Should().Be(expected);
        }

        [Fact]
        public void GetPaymentStatus_PaymentIntent_Should_Return_Captured_When_Succeeded_Without_LatestCharge()
        {
            _provider.MapStatus(new PaymentIntent { Status = "succeeded" }).Should().Be(PaymentStatus.Captured);
        }

        [Fact]
        public void GetPaymentStatus_PaymentIntent_Should_Return_Authorized_When_Requires_Capture()
        {
            _provider.MapStatus(new PaymentIntent { Status = "requires_capture" }).Should().Be(PaymentStatus.Authorized);
        }

        [Fact]
        public void GetPaymentStatus_PaymentIntent_Should_Return_Cancelled_When_Canceled()
        {
            _provider.MapStatus(new PaymentIntent { Status = "canceled" }).Should().Be(PaymentStatus.Cancelled);
        }

        // Exposes the protected status mapping without changing the provider's public API
        private sealed class TestProvider : StripeCheckoutPaymentProvider
        {
            public TestProvider()
                : base(null!, null!)
            {
            }

            public PaymentStatus MapStatus(Charge charge) => GetPaymentStatus(charge);

            public PaymentStatus MapStatus(PaymentIntent paymentIntent) => GetPaymentStatus(paymentIntent);
        }
    }
}

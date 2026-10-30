using FluentAssertions;
using Stripe;
using Stripe.Checkout;
using Umbraco.Commerce.Core.Models;

namespace Umbraco.Commerce.PaymentProviders.Stripe.UnitTests
{
    // Endive shaped payloads for the Stripe objects the provider fetches and reads, to make
    // sure the SDK surfaces the fields the provider depends on and that they map to the
    // expected Commerce payment status.
    public class StripeObjectMappingTests
    {
        private readonly TestProvider _provider = new();

        private const string ChargeJson = """
            {
              "id": "ch_3Qabc123",
              "object": "charge",
              "amount": 10000,
              "amount_captured": 10000,
              "amount_refunded": 0,
              "captured": true,
              "paid": true,
              "refunded": false,
              "status": "succeeded",
              "payment_method_details": { "type": "card", "card": { "brand": "visa", "country": "DK", "last4": "4242" } }
            }
            """;

        [Fact]
        public void Session_Payment_Mode_Should_Expose_Fields_Used_By_The_Provider()
        {
            // preparation
            const string json = """
                {
                  "id": "cs_test_a1B2c3",
                  "object": "checkout.session",
                  "mode": "payment",
                  "status": "complete",
                  "client_reference_id": "7f2b0c2e-5a0e-4f0e-9d58-0c8b1f0c1a11",
                  "customer": "cus_Qabc123",
                  "payment_intent": "pi_3Qabc123",
                  "subscription": null,
                  "ui_mode": "hosted_page",
                  "url": null
                }
                """;

            // execution
            Session session = StripeEntity.FromJson<Session>(json);

            // asserts
            session.Mode.Should().Be("payment");
            session.Status.Should().Be("complete");
            session.ClientReferenceId.Should().Be("7f2b0c2e-5a0e-4f0e-9d58-0c8b1f0c1a11");
            session.CustomerId.Should().Be("cus_Qabc123");
            session.PaymentIntentId.Should().Be("pi_3Qabc123");
            session.SubscriptionId.Should().BeNull();
        }

        [Fact]
        public void Session_Subscription_Mode_Should_Expose_Fields_Used_By_The_Provider()
        {
            // preparation
            const string json = """
                {
                  "id": "cs_test_a1B2c3",
                  "object": "checkout.session",
                  "mode": "subscription",
                  "status": "complete",
                  "customer": "cus_Qabc123",
                  "payment_intent": null,
                  "subscription": "sub_1Qabc123"
                }
                """;

            // execution
            Session session = StripeEntity.FromJson<Session>(json);

            // asserts
            session.Mode.Should().Be("subscription");
            session.SubscriptionId.Should().Be("sub_1Qabc123");
            session.PaymentIntentId.Should().BeNull();
        }

        [Fact]
        public void PaymentIntent_Should_Expose_Metadata_And_Unexpanded_LatestCharge_Id()
        {
            // preparation
            const string json = """
                {
                  "id": "pi_3Qabc123",
                  "object": "payment_intent",
                  "amount": 10000,
                  "currency": "eur",
                  "status": "requires_capture",
                  "customer": "cus_Qabc123",
                  "latest_charge": "ch_3Qabc123",
                  "review": null,
                  "payment_method_types": ["card"],
                  "metadata": { "orderReference": "7f2b0c2e-5a0e-4f0e-9d58-0c8b1f0c1a11" }
                }
                """;

            // execution
            PaymentIntent intent = StripeEntity.FromJson<PaymentIntent>(json);

            // asserts
            intent.CustomerId.Should().Be("cus_Qabc123");
            intent.Metadata.Should().ContainKey("orderReference");
            intent.LatestChargeId.Should().Be("ch_3Qabc123");
            intent.LatestCharge.Should().BeNull();
            _provider.TransactionId(intent).Should().Be("ch_3Qabc123");
            _provider.MapStatus(intent).Should().Be(PaymentStatus.Authorized);
        }

        [Fact]
        public void PaymentIntent_With_Expanded_LatestCharge_Should_Expose_Card_Country_And_Map_To_Captured()
        {
            // preparation
            string json = $$"""
                {
                  "id": "pi_3Qabc123",
                  "object": "payment_intent",
                  "amount": 10000,
                  "currency": "eur",
                  "status": "succeeded",
                  "latest_charge": {{ChargeJson}},
                  "review": null
                }
                """;

            // execution
            PaymentIntent intent = StripeEntity.FromJson<PaymentIntent>(json);

            // asserts
            intent.LatestChargeId.Should().Be("ch_3Qabc123");
            intent.LatestCharge.PaymentMethodDetails.Card.Country.Should().Be("DK");
            _provider.MapStatus(intent).Should().Be(PaymentStatus.Captured);
        }

        [Fact]
        public void PaymentIntent_With_Open_Expanded_Review_Should_Map_To_PendingExternalSystem()
        {
            // preparation
            string json = $$"""
                {
                  "id": "pi_3Qabc123",
                  "object": "payment_intent",
                  "status": "succeeded",
                  "latest_charge": {{ChargeJson}},
                  "review": { "id": "prv_1Qabc123", "object": "review", "open": true, "reason": "rule", "payment_intent": "pi_3Qabc123" }
                }
                """;

            // execution
            PaymentIntent intent = StripeEntity.FromJson<PaymentIntent>(json);

            // asserts
            intent.Review.PaymentIntentId.Should().Be("pi_3Qabc123");
            _provider.MapStatus(intent).Should().Be(PaymentStatus.PendingExternalSystem);
        }

        [Fact]
        public void Review_Should_Expose_PaymentIntent_Id()
        {
            // preparation
            const string json = """
                { "id": "prv_1Qabc123", "object": "review", "open": false, "reason": "approved", "payment_intent": "pi_3Qabc123" }
                """;

            // execution
            Review review = StripeEntity.FromJson<Review>(json);

            // asserts
            review.PaymentIntentId.Should().Be("pi_3Qabc123");
            review.Open.Should().BeFalse();
        }

        [Fact]
        public void Subscription_With_Expanded_Invoice_Payments_Should_Expose_Default_Payment_Intent()
        {
            // preparation
            const string json = """
                {
                  "id": "sub_1Qabc123",
                  "object": "subscription",
                  "status": "active",
                  "latest_invoice": {
                    "id": "in_1Qabc123",
                    "object": "invoice",
                    "status": "paid",
                    "payments": {
                      "object": "list",
                      "has_more": false,
                      "url": "/v1/invoice_payments?invoice=in_1Qabc123",
                      "data": [
                        { "id": "inpay_1Qold", "object": "invoice_payment", "is_default": false, "status": "canceled", "payment": { "type": "payment_intent", "payment_intent": "pi_old" } },
                        { "id": "inpay_1Qabc123", "object": "invoice_payment", "is_default": true, "status": "paid", "payment": { "type": "payment_intent", "payment_intent": "pi_3Qabc123" } }
                      ]
                    }
                  }
                }
                """;

            // execution
            Subscription subscription = StripeEntity.FromJson<Subscription>(json);

            // asserts
            Invoice invoice = subscription.LatestInvoice;
            invoice.Status.Should().Be("paid");
            invoice.Payments.Should().HaveCount(2);

            InvoicePayment? defaultPayment = invoice.Payments.FirstOrDefault(x => x.IsDefault) ?? invoice.Payments.LastOrDefault();
            defaultPayment!.Payment.PaymentIntentId.Should().Be("pi_3Qabc123");
        }

        [Fact]
        public void Refund_Should_Expose_Charge_Id()
        {
            // preparation
            const string json = """
                { "id": "re_3Qabc123", "object": "refund", "amount": 2500, "charge": "ch_3Qabc123", "payment_intent": "pi_3Qabc123", "status": "succeeded" }
                """;

            // execution
            Refund refund = StripeEntity.FromJson<Refund>(json);

            // asserts
            refund.ChargeId.Should().Be("ch_3Qabc123");
            refund.Charge.Should().BeNull();
        }

        [Theory]
        [InlineData("void", PaymentStatus.Cancelled)]
        [InlineData("open", PaymentStatus.Authorized)]
        [InlineData("uncollectible", PaymentStatus.Error)]
        [InlineData("draft", PaymentStatus.Initialized)]
        public void GetPaymentStatus_Invoice_Should_Map_Non_Paid_Statuses(string invoiceStatus, PaymentStatus expected)
        {
            _provider.MapStatus(new Invoice { Status = invoiceStatus }, null).Should().Be(expected);
        }

        [Fact]
        public void GetPaymentStatus_Invoice_Paid_Should_Use_PaymentIntent_Status()
        {
            // preparation
            PaymentIntent intent = new() { Status = "succeeded" };

            // execution
            PaymentStatus status = _provider.MapStatus(new Invoice { Status = "paid" }, intent);

            // asserts
            status.Should().Be(PaymentStatus.Captured);
        }

        [Theory]
        [InlineData("requires_payment_method")]
        [InlineData("requires_confirmation")]
        [InlineData("requires_action")]
        [InlineData("processing")]
        public void GetPaymentStatus_PaymentIntent_Should_Return_Initialized_For_Unfinished_Statuses(string intentStatus)
        {
            _provider.MapStatus(new PaymentIntent { Status = intentStatus }).Should().Be(PaymentStatus.Initialized);
        }

        // Exposes the protected helpers without changing the provider's public API
        private sealed class TestProvider : StripeCheckoutPaymentProvider
        {
            public TestProvider()
                : base(null!, null!)
            {
            }

            public PaymentStatus MapStatus(PaymentIntent paymentIntent) => GetPaymentStatus(paymentIntent);

            public PaymentStatus MapStatus(Invoice invoice, PaymentIntent? paymentIntent) => GetPaymentStatus(invoice, paymentIntent);

            public string TransactionId(PaymentIntent paymentIntent) => GetTransactionId(paymentIntent);
        }
    }
}

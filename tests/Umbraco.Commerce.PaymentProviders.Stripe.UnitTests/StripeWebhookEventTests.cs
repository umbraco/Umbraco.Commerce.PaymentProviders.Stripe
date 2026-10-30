using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Stripe;

namespace Umbraco.Commerce.PaymentProviders.Stripe.UnitTests
{
    public class StripeWebhookEventTests
    {
        private const string Secret = "whsec_test_secret";

        [Theory]
        [InlineData("checkout.session.completed", "cs_test_a1B2c3", "checkout.session")]
        [InlineData("payment_intent.succeeded", "pi_3Qabc123", "payment_intent")]
        [InlineData("review.closed", "prv_1Qabc123", "review")]
        public void Should_Deserialize_Handled_Event_Types(string eventType, string objectId, string objectType)
        {
            // preparation
            string json = BuildEvent(eventType, objectId, objectType, "2026-10-28.endive");

            // execution
            StripeWebhookEvent? stripeEvent = JsonSerializer.Deserialize<StripeWebhookEvent>(json);

            // asserts
            stripeEvent.Should().NotBeNull();
            stripeEvent!.Id.Should().Be("evt_1Qxyz");
            stripeEvent.Type.Should().Be(eventType);
            stripeEvent.Data.Object.Id.Should().Be(objectId);
            stripeEvent.Data.Object.Type.Should().Be(objectType);
            stripeEvent.Data.Object.Instance.Should().BeNull();
        }

        [Theory]
        [InlineData("checkout.session.completed", nameof(EventTypes.CheckoutSessionCompleted))]
        [InlineData("payment_intent.succeeded", nameof(EventTypes.PaymentIntentSucceeded))]
        [InlineData("review.closed", nameof(EventTypes.ReviewClosed))]
        public void Handled_Event_Type_Strings_Should_Match_Sdk_Constants(string eventType, string constantName)
        {
            typeof(EventTypes).GetField(constantName)!.GetRawConstantValue().Should().Be(eventType);
        }

        [Theory]
        [InlineData("2025-11-17.clover")]
        [InlineData("2026-03-25.dahlia")]
        [InlineData("2026-10-28.endive")]
        // The provider validates the signature with EventUtility.ValidateSignature and reads only id, type and the
        // object id itself, rather than EventUtility.ConstructEvent, which throws when the webhook endpoint's API
        // version differs from Stripe.net's pinned one. Keep it that way so merchants needn't upgrade their endpoints.
        public void Should_Deserialize_Regardless_Of_Webhook_Endpoint_Api_Version(string apiVersion)
        {
            // preparation
            string json = BuildEvent("checkout.session.completed", "cs_test_a1B2c3", "checkout.session", apiVersion);

            // execution
            StripeWebhookEvent? stripeEvent = JsonSerializer.Deserialize<StripeWebhookEvent>(json);

            // asserts
            stripeEvent!.Data.Object.Id.Should().Be("cs_test_a1B2c3");
            stripeEvent.Data.Object.Type.Should().Be("checkout.session");
        }

        [Theory]
        [InlineData("2025-11-17.clover")]
        [InlineData("2026-10-28.endive")]
        public void ValidateSignature_Should_Accept_Valid_Signature_For_Any_Api_Version(string apiVersion)
        {
            // preparation
            string json = BuildEvent("payment_intent.succeeded", "pi_3Qabc123", "payment_intent", apiVersion);

            // execution
            Action validate = () => EventUtility.ValidateSignature(json, Sign(json, Secret), Secret);

            // asserts
            validate.Should().NotThrow();
        }

        [Fact]
        public void ValidateSignature_Should_Reject_Signature_From_Another_Secret()
        {
            // preparation
            string json = BuildEvent("payment_intent.succeeded", "pi_3Qabc123", "payment_intent", "2026-10-28.endive");

            // execution
            Action validate = () => EventUtility.ValidateSignature(json, Sign(json, "whsec_other"), Secret);

            // asserts
            validate.Should().Throw<StripeException>();
        }

        private static string BuildEvent(string eventType, string objectId, string objectType, string apiVersion)
        {
            return $$"""
            {
              "id": "evt_1Qxyz",
              "object": "event",
              "api_version": "{{apiVersion}}",
              "created": 1793000000,
              "data": {
                "object": {
                  "id": "{{objectId}}",
                  "object": "{{objectType}}",
                  "livemode": false,
                  "metadata": { "orderReference": "abc" }
                }
              },
              "livemode": false,
              "pending_webhooks": 1,
              "request": { "id": null, "idempotency_key": null },
              "type": "{{eventType}}"
            }
            """;
        }

        private static string Sign(string payload, string secret)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
            string signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

            return $"t={timestamp},v1={signature}";
        }
    }
}

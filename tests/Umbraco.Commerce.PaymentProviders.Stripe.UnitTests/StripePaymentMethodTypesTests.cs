using System.Net;
using FluentAssertions;
using Stripe;
using Stripe.Checkout;

namespace Umbraco.Commerce.PaymentProviders.Stripe.UnitTests
{
    public class StripePaymentMethodTypesTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(",")]
        [InlineData(" , , ")]
        public void Parse_Should_Default_To_Card_When_Setting_Is_Empty(string? setting)
        {
            StripePaymentMethodTypes.Parse(setting).Should().Equal("card");
        }

        [Fact]
        public void Parse_Should_Return_Single_Type()
        {
            StripePaymentMethodTypes.Parse("ideal").Should().Equal("ideal");
        }

        [Fact]
        public void Parse_Should_Split_Comma_Separated_Types_In_Order()
        {
            StripePaymentMethodTypes.Parse("card,ideal,klarna").Should().Equal("card", "ideal", "klarna");
        }

        [Fact]
        public void Parse_Should_Trim_Whitespace_And_Skip_Empty_Entries()
        {
            StripePaymentMethodTypes.Parse(" card , ,ideal,, klarna ").Should().Equal("card", "ideal", "klarna");
        }

        [Fact]
        public async Task Checkout_Session_Request_Should_Send_Allowed_Payment_Method_Types_Not_Payment_Method_Types()
        {
            // preparation
            using RecordingHandler handler = new();
            using HttpClient httpClient = new(handler);
            StripeClient client = new("sk_test_123", httpClient: new SystemNetHttpClient(httpClient));

            // execution
            await Assert.ThrowsAnyAsync<StripeException>(() => new SessionService(client).CreateAsync(
                new SessionCreateOptions
                {
                    Mode = "payment",
                    AllowedPaymentMethodTypes = StripePaymentMethodTypes.Parse("card, ideal"),
                })).ConfigureAwait(false);

            // asserts
            handler.Body.Should().Contain("allowed_payment_method_types[0]=card");
            handler.Body.Should().Contain("allowed_payment_method_types[1]=ideal");
            handler.Body.Should().NotContain("&payment_method_types");
            handler.Body.Should().NotStartWith("payment_method_types");
            handler.StripeVersion.Should().EndWith(".endive");
        }

        [Fact]
        public async Task Payment_Intent_Request_Should_Send_Allowed_Payment_Method_Types_Not_Payment_Method_Types()
        {
            // preparation
            using RecordingHandler handler = new();
            using HttpClient httpClient = new(handler);
            StripeClient client = new("sk_test_123", httpClient: new SystemNetHttpClient(httpClient));

            // execution
            await Assert.ThrowsAnyAsync<StripeException>(() => new PaymentIntentService(client).CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = 1000,
                    Currency = "eur",
                    AllowedPaymentMethodTypes = StripePaymentMethodTypes.Parse(null),
                })).ConfigureAwait(false);

            // asserts
            handler.Body.Should().Contain("allowed_payment_method_types[0]=card");
            handler.Body.Should().NotContain("&payment_method_types");
            handler.Body.Should().NotStartWith("payment_method_types");
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public string Body { get; private set; } = string.Empty;

            public string StripeVersion { get; private set; } = string.Empty;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = request.Content == null ? string.Empty : Uri.UnescapeDataString(await request.Content.ReadAsStringAsync(cancellationToken));
                StripeVersion = request.Headers.TryGetValues("Stripe-Version", out IEnumerable<string>? values) ? string.Join(",", values) : string.Empty;

                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"stub\"}}"),
                };
            }
        }
    }
}

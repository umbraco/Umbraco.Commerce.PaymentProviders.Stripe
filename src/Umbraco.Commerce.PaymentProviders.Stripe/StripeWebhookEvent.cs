using System.Text.Json.Serialization;

namespace Umbraco.Commerce.PaymentProviders.Stripe
{
    // Stripped down Stripe webhook event which should
    // hopefully work regardless of webhook API version.
    // We are essentially grabbing the most basic info
    // and then we use the API to fetch the entity in 
    // question so that it is fetched using the payment
    // providers API version.
    public class StripeWebhookEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("data")]
        public StripeWebhookEventData Data { get; set; }
    }

    public class StripeWebhookEventData
    {
        [JsonPropertyName("object")]
        public StripeWebhookEventObject Object { get; set; }
    }

    public class StripeWebhookEventObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("object")]
        public string Type { get; set; }

        [JsonIgnore]
        public object Instance { get; set; }
    }
}

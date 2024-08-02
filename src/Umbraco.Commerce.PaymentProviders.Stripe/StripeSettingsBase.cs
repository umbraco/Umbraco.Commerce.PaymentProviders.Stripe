using Umbraco.Commerce.Core.PaymentProviders;

namespace Umbraco.Commerce.PaymentProviders.Stripe
{
    public class StripeSettingsBase
    {
        [PaymentProviderSetting(SortOrder = 100)]
        public string ContinueUrl { get; set; }

        [PaymentProviderSetting(SortOrder = 200)]
        public string CancelUrl { get; set; }

        [PaymentProviderSetting(SortOrder = 300)]
        public string ErrorUrl { get; set; }

        [PaymentProviderSetting(
            SortOrder = 400)]
        public string BillingAddressLine1PropertyAlias { get; set; }

        [PaymentProviderSetting(
            SortOrder = 500)]
        public string BillingAddressLine2PropertyAlias { get; set; }

        [PaymentProviderSetting(
            SortOrder = 600)]
        public string BillingAddressCityPropertyAlias { get; set; }

        [PaymentProviderSetting(
            SortOrder = 700)]
        public string BillingAddressStatePropertyAlias { get; set; }

        [PaymentProviderSetting(
            SortOrder = 800)]
        public string BillingAddressZipCodePropertyAlias { get; set; }

        [PaymentProviderSetting(
            SortOrder = 900)]
        public string TestSecretKey { get; set; }

        [PaymentProviderSetting(
            SortOrder = 1000)]
        public string TestPublicKey { get; set; }

        [PaymentProviderSetting(
            SortOrder = 1100)]
        public string TestWebhookSigningSecret { get; set; }

        [PaymentProviderSetting(
            SortOrder = 1200)]
        public string LiveSecretKey { get; set; }

        [PaymentProviderSetting(
            SortOrder = 1300)]
        public string LivePublicKey { get; set; }

        [PaymentProviderSetting(
            SortOrder = 1400)]
        public string LiveWebhookSigningSecret { get; set; }

        [PaymentProviderSetting(
            SortOrder = 10000)]
        public bool TestMode { get; set; }

        // Advanced settings

        [PaymentProviderSetting(
            IsAdvanced = true,
            SortOrder = 1000100)]
        public string OrderHeading { get; set; }

        [PaymentProviderSetting(
            IsAdvanced = true,
            SortOrder = 1000200)]
        public string OrderImage { get; set; }
    }
}

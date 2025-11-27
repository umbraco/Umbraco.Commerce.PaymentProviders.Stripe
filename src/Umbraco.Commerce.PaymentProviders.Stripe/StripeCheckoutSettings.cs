using Umbraco.Commerce.Core.PaymentProviders;

namespace Umbraco.Commerce.PaymentProviders.Stripe
{
    public class StripeCheckoutSettings : StripeSettingsBase
    {
        [PaymentProviderSetting(
            SortOrder = 2000)]
        public bool Capture { get; set; }

        [PaymentProviderSetting(
            SortOrder = 2100)]
        public bool SendStripeReceipt { get; set; }

        // Advanced settings

        [PaymentProviderSetting(
            IsAdvanced = true,
            SortOrder = 1000210)]
        public string OneTimeItemsHeading { get; set; }

        [PaymentProviderSetting(
            IsAdvanced = true,
            SortOrder = 1000300)]
        public string OrderProperties { get; set; }

        [PaymentProviderSetting(
            IsAdvanced = true,
            SortOrder = 1000400)]
        public string PaymentMethodTypes { get; set; }
    }
}

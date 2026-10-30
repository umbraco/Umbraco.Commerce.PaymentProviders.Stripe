#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Umbraco.Commerce.PaymentProviders.Stripe
{
    internal static class StripePaymentMethodTypes
    {
        private const string DefaultPaymentMethodType = "card";

        // Parses the comma separated PaymentMethodTypes setting into the list sent as
        // allowed_payment_method_types. Falls back to card when the setting is empty.
        public static List<string> Parse(string? setting)
        {
            List<string> types = string.IsNullOrWhiteSpace(setting)
                ? []
                : setting.Split(',')
                    .Select(tag => tag.Trim())
                    .Where(tag => !string.IsNullOrEmpty(tag))
                    .ToList();

            return types.Count > 0 ? types : [DefaultPaymentMethodType];
        }
    }
}

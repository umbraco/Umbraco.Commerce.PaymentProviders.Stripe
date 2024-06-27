export default {
    paymentProviders: {
        'stripeCheckoutLabel': 'Stripe Checkout',
        'stripeCheckoutDescription': 'Stripe Checkout payment provider for one time and subscription payments',
        'stripeCheckoutSettingsContinueUrlLabel': 'Continue URL',
        'stripeCheckoutSettingsContinueUrlDescription': 'The URL to continue to after this provider has done processing. eg: /continue/',
        'stripeCheckoutSettingsCancelUrlLabel': 'Cancel URL',
        'stripeCheckoutSettingsCancelUrlDescription': 'The URL to return to if the payment attempt is canceled. eg: /cancel/',
        'stripeCheckoutSettingsErrorUrlLabel': 'Error URL',
        'stripeCheckoutSettingsErrorUrlDescription': 'The URL to return to if the payment attempt errors. eg: /error/',

        'stripeCheckoutSettingsBillingAddressLine1PropertyAliasLabel': 'Billing Address (Line 1) Property Alias',
        'stripeCheckoutSettingsBillingAddressLine1PropertyAliasDescription': '[Required] The order property alias containing line 1 of the billing address',

        'stripeCheckoutSettingsBillingAddressLine2PropertyAliasLabel': 'Billing Address (Line 2) Property Alias',
        'stripeCheckoutSettingsBillingAddressLine2PropertyAliasDescription': 'The order property alias containing line 2 of the billing address',

        'stripeCheckoutSettingsBillingAddressCityPropertyAliasLabel': 'Billing Address City Property Alias',
        'stripeCheckoutSettingsBillingAddressCityPropertyAliasDescription': '[Required] The order property alias containing the city of the billing address',

        'stripeCheckoutSettingsBillingAddressStatePropertyAliasLabel': 'Billing Address State Property Alias',
        'stripeCheckoutSettingsBillingAddressStatePropertyAliasDescription': 'The order property alias containing the state of the billing address',

        'stripeCheckoutSettingsBillingAddressZipCodePropertyAliasLabel': 'Billing Address ZipCode Property Alias',
        'stripeCheckoutSettingsBillingAddressZipCodePropertyAliasDescription': '[Required] The order property alias containing the zip code of the billing address',

        'stripeCheckoutSettingsTestSecretKeyLabel': 'Test Secret Key',
        'stripeCheckoutSettingsTestSecretKeyDescription': 'Your test Stripe secret key',

        'stripeCheckoutSettingsTestPublicKeyLabel': 'Test Public Key',
        'stripeCheckoutSettingsTestPublicKeyDescription': 'Your test Stripe public key',

        'stripeCheckoutSettingsTestWebhookSigningSecretLabel': 'Test Webhook Signing Secret',
        'stripeCheckoutSettingsTestWebhookSigningSecretDescription': 'Your test Stripe webhook signing secret',

        'stripeCheckoutSettingsLiveSecretKeyLabel': 'Live Secret Key',
        'stripeCheckoutSettingsLiveSecretKeyDescription': 'Your live Stripe secret key',

        'stripeCheckoutSettingsLivePublicKeyLabel': 'Live Public Key',
        'stripeCheckoutSettingsLivePublicKeyDescription': 'Your live Stripe public key',

        'stripeCheckoutSettingsLiveWebhookSigningSecretLabel': 'Live Webhook Signing Secret',
        'stripeCheckoutSettingsLiveWebhookSigningSecretDescription': 'Your live Stripe webhook signing secret',

        'stripeCheckoutSettingsTestModeLabel': 'Test Mode',
        'stripeCheckoutSettingsTestModeDescription': 'Set whether to process payments in test mode',

        'stripeCheckoutSettingsCaptureLabel': 'Capture',
        'stripeCheckoutSettingsCaptureDescription': 'Flag indicating whether to immediately capture the payment, or whether to just authorize the payment for later (manual) capture. Only supported when the payment is a non-subscription based payment. Subscription based payments will always be captured immediately',

        'stripeCheckoutSettingsSendStripeReceiptLabel': 'Send Stripe Receipt',
        'stripeCheckoutSettingsSendStripeReceiptDescription': 'Flag indicating whether to send a Stripe receipt to the customer. Receipts are only sent when in live mode',

        // ========== Advanced Settings ==========

        'stripeCheckoutSettingsOrderHeadingLabel': 'Order Heading',
        'stripeCheckoutSettingsOrderHeadingDescription': 'A heading to display on the order summary of the Stripe Checkout screen',

        'stripeCheckoutSettingsOrderImageLabel': 'Order Image',
        'stripeCheckoutSettingsOrderImageDescription': 'The URL of an image to display on the order summary of the Stripe Checkout screen. Should be 480x480px',

        'stripeCheckoutSettingsOneTimeItemsHeadingLabel': 'One-Time Items Heading',
        'stripeCheckoutSettingsOneTimeItemsHeadingDescription': 'A heading to display for the total one-time payment items order line when the order consists of both subscription and one-time payment items',

        'stripeCheckoutSettingsOrderPropertiesLabel': 'Order Properties',
        'stripeCheckoutSettingsOrderPropertiesDescription': 'A comma separated list of order properties to copy to the transactions meta data',

        'stripeCheckoutSettingsPaymentMethodTypesLabel': 'Payment Method Types',
        'stripeCheckoutSettingsPaymentMethodTypesDescription': 'A comma separated list of Stripe payment method types to use. Defaults to just \'card\' if left empty',
    },
};
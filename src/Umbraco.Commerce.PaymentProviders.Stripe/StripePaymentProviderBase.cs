using Newtonsoft.Json;
using Stripe;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Commerce.Common.Logging;
using Umbraco.Commerce.Core.Api;
using Umbraco.Commerce.Core.Models;
using Umbraco.Commerce.Core.PaymentProviders;

using StripeTaxRate = Stripe.TaxRate;

namespace Umbraco.Commerce.PaymentProviders.Stripe
{
    public abstract class StripePaymentProviderBase<TSelf, TSettings> : PaymentProviderBase<TSettings>
        where TSelf : StripePaymentProviderBase<TSelf, TSettings>
        where TSettings : StripeSettingsBase, new()
    {
        protected ILogger<TSelf> Logger { get; }

        private static string[] SUPPORTED_LOCALES = new[]
        {
            "bg","cs","da","de","el","en",
            "en-GB","es","es-419","et","fi","fil",
            "fr","fr-CA","hr","hu","id","it",
            "ja","ko","lt","lv","ms","mt",
            "nb","nl","pl","pt","pt-BR","ro",
            "ru","sk","sl","sv","th","tr",
            "vi","zh","zh-HK","zh-TW"
        };

        public StripePaymentProviderBase(
            UmbracoCommerceContext ctx,
            ILogger<TSelf> logger)
            : base(ctx)
        {
            Logger = logger;
        }

        public override string GetCancelUrl(PaymentProviderContext<TSettings> ctx)
        {
            return ctx.Settings.CancelUrl;
        }

        public override string GetContinueUrl(PaymentProviderContext<TSettings> ctx)
        {
            return ctx.Settings.ContinueUrl; // + (settings.ContinueUrl.Contains("?") ? "&" : "?") + "session_id={CHECKOUT_SESSION_ID}";
        }

        public override string GetErrorUrl(PaymentProviderContext<TSettings> ctx)
        {
            return ctx.Settings.ErrorUrl;
        }

        public override async Task<OrderReference> GetOrderReferenceAsync(PaymentProviderContext<TSettings> ctx, CancellationToken cancellationToken = default)
        {
            try
            {
                var secretKey = ctx.Settings.TestMode ? ctx.Settings.TestSecretKey : ctx.Settings.LiveSecretKey;
                var webhookSigningSecret = ctx.Settings.TestMode ? ctx.Settings.TestWebhookSigningSecret : ctx.Settings.LiveWebhookSigningSecret;

                ConfigureStripe(secretKey);

                var stripeEvent = await GetWebhookStripeEventAsync(ctx, webhookSigningSecret, cancellationToken).ConfigureAwait(false);
                if (stripeEvent != null && stripeEvent.Type == Events.PaymentIntentSucceeded)
                {
                    if (stripeEvent.Data?.Object?.Instance is PaymentIntent paymentIntent && paymentIntent.Metadata.TryGetValue("orderReference", out string value))
                    {
                        return OrderReference.Parse(value);
                    }
                }
                else if (stripeEvent != null && stripeEvent.Type == Events.CheckoutSessionCompleted)
                {
                    if (stripeEvent.Data?.Object?.Instance is Session stripeSession && !string.IsNullOrWhiteSpace(stripeSession.ClientReferenceId))
                    {
                        return OrderReference.Parse(stripeSession.ClientReferenceId);
                    }
                }
                else if (stripeEvent != null && stripeEvent.Type == Events.ReviewClosed)
                {
                    if (stripeEvent.Data?.Object?.Instance is Review stripeReview && !string.IsNullOrWhiteSpace(stripeReview.PaymentIntentId))
                    {
                        var paymentIntentService = new PaymentIntentService();
                        var paymentIntent = paymentIntentService.Get(stripeReview.PaymentIntentId);

                        if (paymentIntent != null && paymentIntent.Metadata.TryGetValue("orderReference", out string value))
                        {
                            return OrderReference.Parse(value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Stripe - GetOrderReference");
            }

            return await base.GetOrderReferenceAsync(ctx).ConfigureAwait(false);
        }

        protected async Task<StripeTaxRate> GetOrCreateStripeTaxRateAsync(PaymentProviderContext<TSettings> ctx, string taxName, decimal percentage, bool inclusive, CancellationToken cancellationToken = default)
        {
            var taxRateService = new TaxRateService();
            var stripeTaxRates = new List<StripeTaxRate>();

            if (ctx.AdditionalData.TryGetValue("UmbracoCommerce_StripeTaxRates", out object value))
            {
                stripeTaxRates = (List<StripeTaxRate>)value;
            }

            if (stripeTaxRates.Count > 0)
            {
                var taxRate = GetStripeTaxRate(stripeTaxRates, taxName, percentage, inclusive);
                if (taxRate != null)
                {
                    return taxRate;
                }
            }

            stripeTaxRates = (await taxRateService.ListAsync(new TaxRateListOptions { Active = true }, cancellationToken: cancellationToken)).ToList();

            if (ctx.AdditionalData.ContainsKey("UmbracoCommerce_StripeTaxRates"))
            {
                ctx.AdditionalData["UmbracoCommerce_StripeTaxRates"] = stripeTaxRates;
            }
            else
            {
                ctx.AdditionalData.Add("UmbracoCommerce_StripeTaxRates", stripeTaxRates);
            }

            if (stripeTaxRates.Count > 0)
            {
                var taxRate = GetStripeTaxRate(stripeTaxRates, taxName, percentage, inclusive);
                if (taxRate != null)
                {
                    return taxRate;
                }
            }

            var newTaxRate = taxRateService.Create(new TaxRateCreateOptions
            {
                DisplayName = taxName,
                Percentage = percentage,
                Inclusive = inclusive,
            });

            stripeTaxRates.Add(newTaxRate);

            ctx.AdditionalData["UmbracoCommerce_StripeTaxRates"] = stripeTaxRates;

            return newTaxRate;
        }

        private StripeTaxRate GetStripeTaxRate(IEnumerable<StripeTaxRate> taxRates, string taxName, decimal percentage, bool inclusive)
        {
            return taxRates.FirstOrDefault(x => x.Percentage == percentage && x.Inclusive == inclusive && x.DisplayName == taxName);
        }

        protected async Task<StripeWebhookEvent> GetWebhookStripeEventAsync(PaymentProviderContext<TSettings> ctx, string webhookSigningSecret, CancellationToken cancellationToken = default)
        {
            StripeWebhookEvent stripeEvent = null;

            if (ctx.AdditionalData.TryGetValue("UmbracoCommerce_StripeEvent", out object value))
            {
                stripeEvent = (StripeWebhookEvent)value;
            }
            else
            {
                try
                {
                    var stream = await ctx.Request.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                    if (stream.CanSeek)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                        var stripeSignature = ctx.Request.Headers.GetValues("Stripe-Signature").FirstOrDefault();

                        // Just validate the webhook signature
                        EventUtility.ValidateSignature(json, stripeSignature, webhookSigningSecret);

                        // Parse the event ourselves to our custom webhook event model
                        // as it only captures minimal object information.
                        stripeEvent = JsonConvert.DeserializeObject<StripeWebhookEvent>(json);

                        // We manually fetch the event object type ourself as it means it will be fetched
                        // using the same API version as the payment providers is coded against.
                        // NB: Only supports a number of object types we are likely to be interested in.
                        if (stripeEvent?.Data?.Object != null)
                        {
                            switch (stripeEvent.Data.Object.Type)
                            {
                                case "checkout.session":
                                    var sessionService = new SessionService();
                                    stripeEvent.Data.Object.Instance = await sessionService.GetAsync(stripeEvent.Data.Object.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                                    break;
                                case "charge":
                                    var chargeService = new ChargeService();
                                    stripeEvent.Data.Object.Instance = await chargeService.GetAsync(stripeEvent.Data.Object.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                                    break;
                                case "payment_intent":
                                    var paymentIntentService = new PaymentIntentService();
                                    stripeEvent.Data.Object.Instance = await paymentIntentService.GetAsync(stripeEvent.Data.Object.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                                    break;
                                case "subscription":
                                    var subscriptionService = new SubscriptionService();
                                    stripeEvent.Data.Object.Instance = await subscriptionService.GetAsync(stripeEvent.Data.Object.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                                    break;
                                case "invoice":
                                    var invoiceService = new InvoiceService();
                                    stripeEvent.Data.Object.Instance = await invoiceService.GetAsync(stripeEvent.Data.Object.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                                    break;
                                case "review":
                                    var reviewService = new ReviewService();
                                    stripeEvent.Data.Object.Instance = await reviewService.GetAsync(stripeEvent.Data.Object.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                                    break;
                            }
                        }

                        ctx.AdditionalData.Add("UmbracoCommerce_StripeEvent", stripeEvent);

                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Stripe - GetWebhookStripeEvent");
                }
            }

            return stripeEvent;
        }

        protected static void ConfigureStripe(string apiKey)
        {
            StripeConfiguration.ApiKey = apiKey;
            StripeConfiguration.MaxNetworkRetries = 2;
        }

        protected string GetTransactionId(PaymentIntent paymentIntent)
        {
            return paymentIntent?.LatestChargeId;
        }

        protected string GetTransactionId(Invoice invoice)
        {
            return invoice?.ChargeId;
        }

        protected string GetTransactionId(Charge charge)
        {
            return charge?.Id;
        }

        protected PaymentStatus GetPaymentStatus(Invoice invoice)
        {
            // Possible Invoice statuses:
            // - draft
            // - open
            // - paid
            // - void
            // - uncollectible

            if (invoice.Status == "void")
                return PaymentStatus.Cancelled;

            if (invoice.Status == "open")
                return PaymentStatus.Authorized;

            if (invoice.Status == "paid")
            {
                if (invoice.PaymentIntent != null)
                    return GetPaymentStatus(invoice.PaymentIntent);

                if (invoice.Charge != null)
                    return GetPaymentStatus(invoice.Charge);

                return PaymentStatus.Captured;
            }

            if (invoice.Status == "uncollectible")
                return PaymentStatus.Error;

            return PaymentStatus.Initialized;
        }

        protected PaymentStatus GetPaymentStatus(PaymentIntent paymentIntent)
        {
            // Possible PaymentIntent statuses:
            // - requires_payment_method
            // - requires_confirmation
            // - requires_action
            // - processing
            // - requires_capture
            // - canceled
            // - succeeded

            if (paymentIntent.Status == "canceled")
                return PaymentStatus.Cancelled;

            // Need this to occur before authorize / succeeded checks
            if (paymentIntent.Review != null && paymentIntent.Review.Open)
                return PaymentStatus.PendingExternalSystem;

            if (paymentIntent.Status == "requires_capture")
                return PaymentStatus.Authorized;

            if (paymentIntent.Status == "succeeded")
            {
                if (paymentIntent.LatestCharge != null)
                {
                    return GetPaymentStatus(paymentIntent.LatestCharge);
                }
                else
                {
                    return PaymentStatus.Captured;
                }
            }

            return PaymentStatus.Initialized;
        }

        protected PaymentStatus GetPaymentStatus(Charge charge)
        {
            PaymentStatus paymentState = PaymentStatus.Initialized;

            if (charge == null)
                return paymentState;

            if (charge.Paid)
            {
                paymentState = PaymentStatus.Authorized;

                if (charge.Captured)
                {
                    paymentState = PaymentStatus.Captured;

                    if (charge.Refunded)
                    {
                        paymentState = PaymentStatus.Refunded;
                    }
                }
                else
                {
                    if (charge.Refunded)
                    {
                        paymentState = PaymentStatus.Cancelled;
                    }
                }
            }

            return paymentState;
        }

        protected string FindBestMatchSupportedLocale(string locale)
        {
            var exactMatch = SUPPORTED_LOCALES.FirstOrDefault(x => x.Equals(locale, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactMatch))
                return exactMatch;

            var countryLocale = locale.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries)[0];
            var subMatch = SUPPORTED_LOCALES.FirstOrDefault(x => x.Equals(countryLocale, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(subMatch))
                return subMatch;

            return "auto";
        }
    }
}

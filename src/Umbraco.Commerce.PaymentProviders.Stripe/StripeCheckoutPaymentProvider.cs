#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using Umbraco.Commerce.Common.Logging;
using Umbraco.Commerce.Core;
using Umbraco.Commerce.Core.Api;
using Umbraco.Commerce.Core.Models;
using Umbraco.Commerce.Core.PaymentProviders;
using Umbraco.Commerce.Extensions;
using CustomerService = Stripe.CustomerService;
using RefundService = Stripe.RefundService;

namespace Umbraco.Commerce.PaymentProviders.Stripe
{
    [PaymentProvider("stripe-checkout")]
    public class StripeCheckoutPaymentProvider : StripePaymentProviderBase<StripeCheckoutPaymentProvider, StripeCheckoutSettings>
    {
        public StripeCheckoutPaymentProvider(
            UmbracoCommerceContext ctx,
            ILogger<StripeCheckoutPaymentProvider> logger)
            : base(ctx, logger)
        {
        }

        public override bool CanFetchPaymentStatus => true;
        public override bool CanCapturePayments => true;
        public override bool CanCancelPayments => true;
        public override bool CanRefundPayments => true;
        public override bool CanPartiallyRefundPayments => true;

        // Don't finalize at continue as we will finalize async via webhook
        public override bool FinalizeAtContinueUrl => false;

        public override IEnumerable<TransactionMetaDataDefinition> TransactionMetaDataDefinitions => new[]{
            new TransactionMetaDataDefinition("stripeSessionId"),
            new TransactionMetaDataDefinition("stripeCustomerId"),
            new TransactionMetaDataDefinition("stripePaymentIntentId"),
            new TransactionMetaDataDefinition("stripeSubscriptionId"),
            new TransactionMetaDataDefinition("stripeChargeId"),
            new TransactionMetaDataDefinition("stripeCardCountry"),
        };

        public override async Task<PaymentFormResult> GenerateFormAsync(PaymentProviderContext<StripeCheckoutSettings> ctx, CancellationToken cancellationToken = default)
        {
            var secretKey = ctx.Settings.TestMode ? ctx.Settings.TestSecretKey : ctx.Settings.LiveSecretKey;
            var publicKey = ctx.Settings.TestMode ? ctx.Settings.TestPublicKey : ctx.Settings.LivePublicKey;

            ConfigureStripe(secretKey);

            var currency = await Context.Services.CurrencyService.GetCurrencyAsync(ctx.Order.CurrencyId);

            var customer = await GetOrCreateStripeCustomerAsync(ctx);
            var metaData = CreateOrderStripeMetaData(ctx);

            var hasRecurringItems = false;
            long recurringTotalPrice = 0;
            long orderTotalPrice = AmountToMinorUnits(ctx.Order.TransactionAmount.Value);

            var lineItems = new List<SessionLineItemOptions>();

            foreach (var orderLine in ctx.Order.OrderLines.Where(IsRecurringOrderLine))
            {
                var orderLineTaxRate = orderLine.TaxRate * 100;

                var lineItemOpts = new SessionLineItemOptions();

                if (orderLine.Properties.ContainsKey("stripePriceId") && !string.IsNullOrWhiteSpace(orderLine.Properties["stripePriceId"]))
                {
                    // NB: When using stripe prices there is an inherit risk that values may not
                    // actually be in sync and so the price displayed on the site might not match
                    // that in stripe and so this may cause inconsistant payments
                    lineItemOpts.Price = orderLine.Properties["stripePriceId"].Value;

                    // If we are using a stripe price, then assume the quantity of the line item means
                    // the quantity of the stripe price you want to buy.
                    lineItemOpts.Quantity = (long)orderLine.Quantity;

                    // Because we are in charge of what taxes apply, we need to setup a tax rate
                    // to ensure the price defined in stripe has the relevant taxes applied
                    var stripePricesIncludeTax = PropertyIsTrue(orderLine.Properties, "stripePriceIncludesTax");
                    var stripeTaxRate = await GetOrCreateStripeTaxRateAsync(ctx, "Subscription Tax", orderLineTaxRate, stripePricesIncludeTax, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (stripeTaxRate != null)
                    {
                        lineItemOpts.TaxRates = new List<string>(new[] { stripeTaxRate.Id });
                    }
                }
                else
                {
                    // We don't have a stripe price defined on the ctx.Order line
                    // so we'll create one on the fly using the ctx.Order lines total
                    // value
                    var priceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = currency.Code,
                        UnitAmount = AmountToMinorUnits(orderLine.TotalPrice.Value.WithoutTax / orderLine.Quantity), // Without tax as Stripe will apply the tax
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = orderLine.Properties["stripeRecurringInterval"].Value.ToLowerInvariant(),
                            IntervalCount = long.TryParse(orderLine.Properties["stripeRecurringIntervalCount"], out var intervalCount) ? intervalCount : 1
                        }
                    };

                    if (orderLine.Properties.ContainsKey("stripeProductId") && !string.IsNullOrWhiteSpace(orderLine.Properties["stripeProductId"]))
                    {
                        priceData.Product = orderLine.Properties["stripeProductId"].Value;
                    }
                    else
                    {
                        priceData.ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = orderLine.Name,
                            Metadata = new Dictionary<string, string>
                            {
                                { "productReference", orderLine.ProductReference }
                            }
                        };
                    }

                    lineItemOpts.PriceData = priceData;

                    // For dynamic subscriptions, regardless of line item quantity, treat the line
                    // as a single subscription item with one price being the line items total price
                    lineItemOpts.Quantity = (long)orderLine.Quantity;

                    // If we define the price, then create tax rates that are set to be inclusive
                    // as this means that we can pass prices inclusive of tax and Stripe works out
                    // the pre-tax price which would be less suseptable to rounding inconsistancies
                    var stripeTaxRate = await GetOrCreateStripeTaxRateAsync(ctx, "Subscription Tax", orderLineTaxRate, false, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (stripeTaxRate != null)
                    {
                        lineItemOpts.TaxRates = new List<string>(new[] { stripeTaxRate.Id });
                    }
                }

                lineItems.Add(lineItemOpts);

                recurringTotalPrice += AmountToMinorUnits(orderLine.TotalPrice.Value.WithTax);
                hasRecurringItems = true;
            }

            if (recurringTotalPrice < orderTotalPrice)
            {
                // If the total value of the ctx.Order is not covered by the subscription items
                // then we add another line item for the remainder of the ctx.Order value

                var lineItemOpts = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = currency.Code,
                        UnitAmount = orderTotalPrice - recurringTotalPrice,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = hasRecurringItems
                                ? !string.IsNullOrWhiteSpace(ctx.Settings.OneTimeItemsHeading) ? ctx.Settings.OneTimeItemsHeading : "One time items (inc Tax)"
                                : !string.IsNullOrWhiteSpace(ctx.Settings.OrderHeading) ? ctx.Settings.OrderHeading : "#" + ctx.Order.OrderNumber,
                            Description = hasRecurringItems || !string.IsNullOrWhiteSpace(ctx.Settings.OrderHeading) ? "#" + ctx.Order.OrderNumber : null,
                        }
                    },
                    Quantity = 1
                };

                lineItems.Add(lineItemOpts);
            }

            // Add image to the first item (only if it's not a product link)
            if (!string.IsNullOrWhiteSpace(ctx.Settings.OrderImage) && lineItems.Count > 0 && lineItems[0].PriceData?.ProductData != null)
            {
                lineItems[0].PriceData.ProductData.Images = new[] { ctx.Settings.OrderImage }.ToList();
            }

            var sessionOptions = new SessionCreateOptions
            {
                Customer = customer.Id,
                PaymentMethodTypes = !string.IsNullOrWhiteSpace(ctx.Settings.PaymentMethodTypes)
                    ? ctx.Settings.PaymentMethodTypes.Split(',')
                        .Select(tag => tag.Trim())
                        .Where(tag => !string.IsNullOrEmpty(tag))
                        .ToList()
                    : new List<string> {
                        "card",
                    },
                LineItems = lineItems,
                Mode = hasRecurringItems
                    ? "subscription"
                    : "payment",
                ClientReferenceId = ctx.Order.GenerateOrderReference(),
                SuccessUrl = ctx.Urls.ContinueUrl,
                CancelUrl = ctx.Urls.CancelUrl,
                Locale = FindBestMatchSupportedLocale(ctx.Order.LanguageIsoCode)
            };

            if (hasRecurringItems)
            {
                sessionOptions.SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = metaData
                };
            }
            else
            {
                sessionOptions.PaymentIntentData = new SessionPaymentIntentDataOptions
                {
                    CaptureMethod = ctx.Settings.Capture ? "automatic" : "manual",
                    Metadata = metaData
                };
            }

            if (ctx.Settings.SendStripeReceipt)
            {
                sessionOptions.PaymentIntentData.ReceiptEmail = ctx.Order.CustomerInfo.Email;
            }

            var sessionService = new SessionService();
            var session = await sessionService.CreateAsync(sessionOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

            return new PaymentFormResult()
            {
                MetaData = new Dictionary<string, string>
                {
                    { "stripeSessionId", session.Id },
                    { "stripeCustomerId", session.CustomerId }
                },
                Form = new PaymentForm(ctx.Urls.ErrorUrl, PaymentFormMethod.Post)
                    .WithAttribute("onsubmit", "return handleStripeCheckout(event)")
                    .WithJs(@"
                        window.handleStripeCheckout = function (e) {
                            e.preventDefault();
                            window.location.href = '" + session.Url + @"';
                            return false;
                        }
                    "),
            };
        }

        public override async Task<CallbackResult> ProcessCallbackAsync(PaymentProviderContext<StripeCheckoutSettings> context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.HttpContext.Request.GetEncodedPathAndQuery().Contains("create=paymentIntent", StringComparison.InvariantCultureIgnoreCase))
            {
                return await ProcessCreatePaymentIntentCallbackAsync(context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return await ProcessWebhookCallbackAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<CallbackResult> ProcessCreatePaymentIntentCallbackAsync(PaymentProviderContext<StripeCheckoutSettings> ctx, CancellationToken cancellationToken = default)
        {
            var secretKey = ctx.Settings.TestMode ? ctx.Settings.TestSecretKey : ctx.Settings.LiveSecretKey;

            ConfigureStripe(secretKey);

            var currency = await Context.Services.CurrencyService.GetCurrencyAsync(ctx.Order.CurrencyId);

            var customer = await GetOrCreateStripeCustomerAsync(ctx);
            var metaData = CreateOrderStripeMetaData(ctx);

            var paymentIntentService = new PaymentIntentService();
            var paymentIntentOptions = new PaymentIntentCreateOptions
            {
                Customer = customer?.Id,
                Amount = AmountToMinorUnits(ctx.Order.TransactionAmount.Value),
                Currency = currency.Code,
                //AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                //{
                //    Enabled = true,
                //},
                PaymentMethodTypes = !string.IsNullOrWhiteSpace(ctx.Settings.PaymentMethodTypes)
                    ? ctx.Settings.PaymentMethodTypes.Split(',')
                        .Select(tag => tag.Trim())
                        .Where(tag => !string.IsNullOrEmpty(tag))
                        .ToList()
                    : new List<string> {
                        "card",
                    },
                Metadata = metaData
            };

            var paymentIntent = await paymentIntentService.CreateAsync(paymentIntentOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

            return new CallbackResult
            {
                ActionResult = new JsonResult(new { clientSecret = paymentIntent.ClientSecret })
            };
        }

        private async Task<CallbackResult> ProcessWebhookCallbackAsync(PaymentProviderContext<StripeCheckoutSettings> ctx, CancellationToken cancellationToken = default)
        {
            // The ProcessCallback method is only intendid to be called via a Stripe Webhook and so
            // it's job is to process the webhook event and finalize / update the ctx.Order accordingly

            try
            {
                var secretKey = ctx.Settings.TestMode ? ctx.Settings.TestSecretKey : ctx.Settings.LiveSecretKey;
                var webhookSigningSecret = ctx.Settings.TestMode ? ctx.Settings.TestWebhookSigningSecret : ctx.Settings.LiveWebhookSigningSecret;

                ConfigureStripe(secretKey);

                var stripeEvent = await GetWebhookStripeEventAsync(ctx, webhookSigningSecret, cancellationToken).ConfigureAwait(false);
                if (stripeEvent != null && stripeEvent.Type == EventTypes.PaymentIntentSucceeded)
                {
                    if (stripeEvent.Data?.Object?.Instance is PaymentIntent paymentIntent)
                    {
                        return CallbackResult.Ok(
                            new TransactionInfo
                            {
                                TransactionId = GetTransactionId(paymentIntent),
                                AmountAuthorized = AmountFromMinorUnits(paymentIntent.Amount),
                                PaymentStatus = GetPaymentStatus(paymentIntent)
                            },
                            new Dictionary<string, string>
                            {
                                { "stripeCustomerId", paymentIntent.CustomerId },
                                { "stripePaymentIntentId", paymentIntent.Id },
                                { "stripeChargeId", GetTransactionId(paymentIntent) },
                                { "stripeCardCountry", paymentIntent.LatestCharge?.PaymentMethodDetails?.Card?.Country }
                            }
                        );
                    }
                }
                else if (stripeEvent != null && stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
                {
                    if (stripeEvent.Data?.Object?.Instance is Session stripeSession)
                    {
                        if (stripeSession.Mode == "payment")
                        {
                            var paymentIntentService = new PaymentIntentService();
                            var paymentIntent = await paymentIntentService.GetAsync(
                                stripeSession.PaymentIntentId,
                                new PaymentIntentGetOptions
                                {
                                    Expand = new List<string>(new[]
                                    {
                                        "latest_charge",
                                        "review"
                                    })
                                },
                                cancellationToken: cancellationToken).ConfigureAwait(false);

                            return CallbackResult.Ok(
                                new TransactionInfo
                                {
                                    TransactionId = GetTransactionId(paymentIntent),
                                    AmountAuthorized = AmountFromMinorUnits(paymentIntent.Amount),
                                    PaymentStatus = GetPaymentStatus(paymentIntent)
                                },
                                new Dictionary<string, string>
                                {
                                    { "stripeSessionId", stripeSession.Id },
                                    { "stripeCustomerId", stripeSession.CustomerId },
                                    { "stripePaymentIntentId", stripeSession.PaymentIntentId },
                                    { "stripeSubscriptionId", stripeSession.SubscriptionId },
                                    { "stripeChargeId", GetTransactionId(paymentIntent) },
                                    { "stripeCardCountry", paymentIntent.LatestCharge?.PaymentMethodDetails?.Card?.Country }
                                }
                            );
                        }
                        else if (stripeSession.Mode == "subscription")
                        {
                            var subscriptionService = new SubscriptionService();
                            var subscription = await subscriptionService.GetAsync(
                                stripeSession.SubscriptionId,
                                new SubscriptionGetOptions
                                {
                                    Expand = new List<string>(new[]
                                    {
                                        "latest_invoice",
                                        "latest_invoice.payments",
                                        "latest_invoice.payments.payment",
                                        "latest_invoice.payments.payment.payment_intent",
                                        "latest_invoice.payments.payment.payment_intent.review",
                                        "latest_invoice.payments.payment.charge",
                                        "latest_invoice.payments.payment.charge.review"
                                    })
                                },
                                cancellationToken: cancellationToken).ConfigureAwait(false);

                            var invoice = subscription.LatestInvoice;
                            var lastPayment = invoice.Payments.LastOrDefault()?.Payment;
                            if (lastPayment != null)
                            {
                                return CallbackResult.Ok(
                                    new TransactionInfo
                                    {
                                        TransactionId = GetTransactionId(invoice),
                                        AmountAuthorized = AmountFromMinorUnits(lastPayment.PaymentIntent.Amount),
                                        PaymentStatus = GetPaymentStatus(invoice)
                                    },
                                    new Dictionary<string, string>
                                    {
                                        { "stripeSessionId", stripeSession.Id },
                                        { "stripeCustomerId", stripeSession.CustomerId },
                                        { "stripePaymentIntentId", lastPayment.PaymentIntentId },
                                        { "stripeSubscriptionId", stripeSession.SubscriptionId },
                                        { "stripeChargeId", lastPayment.ChargeId },
                                        { "stripeCardCountry", lastPayment.Charge?.PaymentMethodDetails?.Card?.Country ?? "Unknown" }
                                    }
                                );
                            }
                        }
                    }
                    else if (stripeEvent != null && stripeEvent.Type == EventTypes.ReviewClosed)
                    {
                        if (stripeEvent.Data?.Object?.Instance is Review stripeReview && !string.IsNullOrWhiteSpace(stripeReview.PaymentIntentId))
                        {
                            var paymentIntentService = new PaymentIntentService();
                            var paymentIntent = await paymentIntentService.GetAsync(
                                stripeReview.PaymentIntentId,
                                new PaymentIntentGetOptions
                                {
                                    Expand = new List<string>(new[]
                                    {
                                        "latest_charge",
                                        "review"
                                    })
                                },
                                cancellationToken: cancellationToken).ConfigureAwait(false);

                            return CallbackResult.Ok(
                                new TransactionInfo
                                {
                                    TransactionId = GetTransactionId(paymentIntent),
                                    AmountAuthorized = AmountFromMinorUnits(paymentIntent.Amount),
                                    PaymentStatus = GetPaymentStatus(paymentIntent)
                                }
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Stripe - ProcessCallback");
            }

            return CallbackResult.BadRequest();
        }

        public override async Task<ApiResult> FetchPaymentStatusAsync(PaymentProviderContext<StripeCheckoutSettings> ctx, CancellationToken cancellationToken = default)
        {
            try
            {
                var secretKey = ctx.Settings.TestMode ? ctx.Settings.TestSecretKey : ctx.Settings.LiveSecretKey;

                ConfigureStripe(secretKey);

                // See if we have a payment intent to work from
                var paymentIntentId = ctx.Order.Properties["stripePaymentIntentId"];
                if (!string.IsNullOrWhiteSpace(paymentIntentId))
                {
                    var paymentIntentService = new PaymentIntentService();
                    var paymentIntent = await paymentIntentService.GetAsync(paymentIntentId, new PaymentIntentGetOptions
                    {
                        Expand = new List<string>(new[]
                        {
                            "latest_charge",
                            "review"
                        })
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                    return new ApiResult()
                    {
                        TransactionInfo = new TransactionInfoUpdate()
                        {
                            TransactionId = GetTransactionId(paymentIntent),
                            PaymentStatus = GetPaymentStatus(paymentIntent)
                        }
                    };
                }

                // No payment intent, so look for a charge
                var chargeId = ctx.Order.Properties["stripeChargeId"];
                if (!string.IsNullOrWhiteSpace(chargeId))
                {
                    var chargeService = new ChargeService();
                    var charge = await chargeService.GetAsync(chargeId, cancellationToken: cancellationToken).ConfigureAwait(false);

                    return new ApiResult()
                    {
                        TransactionInfo = new TransactionInfoUpdate()
                        {
                            TransactionId = GetTransactionId(charge),
                            PaymentStatus = GetPaymentStatus(charge)
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Stripe - FetchPaymentStatus");
            }

            return ApiResult.Empty;
        }

        public override async Task<ApiResult> CapturePaymentAsync(PaymentProviderContext<StripeCheckoutSettings> ctx, CancellationToken cancellationToken = default)
        {
            // NOTE: Subscriptions aren't currently abled to be "authorized" so the capture
            // routine shouldn't be relevant for subscription payments at this point

            try
            {
                // We can only capture a payment intent, so make sure we have one
                // otherwise there is nothing we can do
                var paymentIntentId = ctx.Order.Properties["stripePaymentIntentId"];
                if (string.IsNullOrWhiteSpace(paymentIntentId))
                    return null;

                var secretKey = ctx.Settings.TestMode ? ctx.Settings.TestSecretKey : ctx.Settings.LiveSecretKey;

                ConfigureStripe(secretKey);

                var paymentIntentService = new PaymentIntentService();
                var paymentIntentOptions = new PaymentIntentCaptureOptions
                {
                    AmountToCapture = AmountToMinorUnits(ctx.Order.TransactionInfo.AmountAuthorized.Value)
                };
                var paymentIntent = await paymentIntentService.CaptureAsync(paymentIntentId, paymentIntentOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

                return new ApiResult()
                {
                    TransactionInfo = new TransactionInfoUpdate()
                    {
                        TransactionId = GetTransactionId(paymentIntent),
                        PaymentStatus = GetPaymentStatus(paymentIntent)
                    },
                    MetaData = new Dictionary<string, string>
                    {
                        { "stripeChargeId", GetTransactionId(paymentIntent) },
                        { "stripeCardCountry", paymentIntent.LatestCharge?.PaymentMethodDetails?.Card?.Country }
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Stripe - CapturePayment");
            }

            return ApiResult.Empty;
        }

        [Obsolete("Will be removed in v17. Use the overload that takes an order refund request")]
        public override async Task<ApiResult?> RefundPaymentAsync(PaymentProviderContext<StripeCheckoutSettings> context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            StoreReadOnly store = await Context.Services.StoreService.GetStoreAsync(context.Order.StoreId);
            Amount refundAmount = store.CanRefundTransactionFee ? context.Order.TransactionInfo.AmountAuthorized + context.Order.TransactionInfo.TransactionFee : context.Order.TransactionInfo.AmountAuthorized;
            return await this.RefundPaymentAsync(
                context,
                new PaymentProviderOrderRefundRequest
                {
                    RefundAmount = refundAmount,
                    Orderlines = [],
                },
                cancellationToken);
        }

        public override async Task<ApiResult?> RefundPaymentAsync(
            PaymentProviderContext<StripeCheckoutSettings> context,
            PaymentProviderOrderRefundRequest refundRequest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(refundRequest);

            try
            {
                // We can only refund a captured charge, so make sure we have one
                // otherwise there is nothing we can do
                PropertyValue chargeId = context.Order.Properties["stripeChargeId"];
                if (string.IsNullOrWhiteSpace(chargeId))
                {
                    return null;
                }

                string secretKey = context.Settings.TestMode ? context.Settings.TestSecretKey : context.Settings.LiveSecretKey;
                ConfigureStripe(secretKey);

                var refundCreateOptions = new RefundCreateOptions()
                {
                    Charge = chargeId,
                    Amount = AmountToMinorUnits(refundRequest.RefundAmount),
                };

                RefundService refundService = new();
                Refund refund = await refundService.CreateAsync(refundCreateOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
                Charge charge = refund.Charge ?? await new ChargeService().GetAsync(refund.ChargeId, cancellationToken: cancellationToken).ConfigureAwait(false);

                // If we have a subscription then we'll cancel it as refunding an ctx.Order
                // should effecitvely undo any purchase
                if (!string.IsNullOrWhiteSpace(context.Order.Properties["stripeSubscriptionId"]))
                {
                    SubscriptionService subscriptionService = new();
                    Subscription subscription = await subscriptionService.GetAsync(context.Order.Properties["stripeSubscriptionId"], cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (subscription != null)
                    {
                        await subscriptionService.CancelAsync(
                            context.Order.Properties["stripeSubscriptionId"],
                            new SubscriptionCancelOptions
                            {
                                InvoiceNow = false,
                                Prorate = false,
                            },
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }

                return new ApiResult()
                {
                    TransactionInfo = new TransactionInfoUpdate()
                    {
                        TransactionId = GetTransactionId(charge),
                        PaymentStatus = GetPaymentStatus(charge)
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Stripe - RefundPayment");
            }

            return ApiResult.Empty;
        }

        public override async Task<ApiResult> CancelPaymentAsync(PaymentProviderContext<StripeCheckoutSettings> ctx, CancellationToken cancellationToken = default)
        {
            // NOTE: Subscriptions aren't currently abled to be "authorized" so the cancel
            // routine shouldn't be relevant for subscription payments at this point

            try
            {
                // See if there is a payment intent to cancel
                var stripePaymentIntentId = ctx.Order.Properties["stripePaymentIntentId"];
                if (!string.IsNullOrWhiteSpace(stripePaymentIntentId))
                {
                    var secretKey = ctx.Settings.TestMode ? ctx.Settings.TestSecretKey : ctx.Settings.LiveSecretKey;

                    ConfigureStripe(secretKey);

                    var paymentIntentService = new PaymentIntentService();
                    var intent = await paymentIntentService.CancelAsync(stripePaymentIntentId, cancellationToken: cancellationToken).ConfigureAwait(false);

                    return new ApiResult()
                    {
                        TransactionInfo = new TransactionInfoUpdate()
                        {
                            TransactionId = GetTransactionId(intent),
                            PaymentStatus = GetPaymentStatus(intent)
                        }
                    };
                }

                // If there is a charge, then it's too late to cancel
                // so we attempt to refund it instead
                var chargeId = ctx.Order.Properties["stripeChargeId"];
                if (chargeId != null)
                    return await RefundPaymentAsync(ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Stripe - CancelPayment");
            }

            return ApiResult.Empty;
        }

        private bool IsRecurringOrderLine(OrderLineReadOnly orderLine)
        {
            return PropertyIsTrue(orderLine.Properties, Constants.Properties.Product.IsRecurringPropertyAlias);
        }

        private bool PropertyIsTrue(IReadOnlyDictionary<string, PropertyValue> props, string propAlias)
        {
            return props.ContainsKey(propAlias)
                && !string.IsNullOrWhiteSpace(props[propAlias])
                && (props[propAlias] == "1" || props[propAlias].Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<Customer> GetOrCreateStripeCustomerAsync(PaymentProviderContext<StripeCheckoutSettings> ctx)
        {
            Customer customer;

            var customerService = new CustomerService();

            var billingCountry = ctx.Order.PaymentInfo.CountryId.HasValue
                ? await Context.Services.CountryService.GetCountryAsync(ctx.Order.PaymentInfo.CountryId.Value)
                : null;

            if (!string.IsNullOrWhiteSpace(ctx.Order.Properties["stripeCustomerId"]))
            {
                var customerOptions = new CustomerUpdateOptions
                {
                    Name = $"{ctx.Order.CustomerInfo.FirstName} {ctx.Order.CustomerInfo.LastName}",
                    Email = ctx.Order.CustomerInfo.Email,
                    Description = ctx.Order.OrderNumber,
                    Address = new AddressOptions
                    {
                        Line1 = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressLine1PropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressLine1PropertyAlias] : "",
                        Line2 = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressLine2PropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressLine2PropertyAlias] : "",
                        City = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressCityPropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressCityPropertyAlias] : "",
                        State = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressStatePropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressStatePropertyAlias] : "",
                        PostalCode = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressZipCodePropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressZipCodePropertyAlias] : "",
                        Country = billingCountry?.Code
                    }
                };

                // Pass billing country / zipcode as meta data as currently
                // this is the only way it can be validated via Radar
                // Block if ::customer:billingCountry:: != :card_country:
                customerOptions.Metadata = new Dictionary<string, string>
                {
                    { "billingCountry", customerOptions.Address.Country },
                    { "billingZipCode", customerOptions.Address.PostalCode }
                };

                customer = customerService.Update(ctx.Order.Properties["stripeCustomerId"].Value, customerOptions);
            }
            else
            {
                var customerOptions = new CustomerCreateOptions
                {
                    Name = $"{ctx.Order.CustomerInfo.FirstName} {ctx.Order.CustomerInfo.LastName}",
                    Email = ctx.Order.CustomerInfo.Email,
                    Description = ctx.Order.OrderNumber,
                    Address = new AddressOptions
                    {
                        Line1 = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressLine1PropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressLine1PropertyAlias] : "",
                        Line2 = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressLine2PropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressLine2PropertyAlias] : "",
                        City = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressCityPropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressCityPropertyAlias] : "",
                        State = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressStatePropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressStatePropertyAlias] : "",
                        PostalCode = !string.IsNullOrWhiteSpace(ctx.Settings.BillingAddressZipCodePropertyAlias)
                            ? ctx.Order.Properties[ctx.Settings.BillingAddressZipCodePropertyAlias] : "",
                        Country = billingCountry?.Code
                    }
                };

                // Pass billing country / zipcode as meta data as currently
                // this is the only way it can be validated via Radar
                // Block if ::customer:billingCountry:: != :card_country:
                customerOptions.Metadata = new Dictionary<string, string>
                {
                    { "billingCountry", customerOptions.Address.Country },
                    { "billingZipCode", customerOptions.Address.PostalCode }
                };

                customer = customerService.Create(customerOptions);
            }

            return customer;
        }

        private Dictionary<string, string> CreateOrderStripeMetaData(PaymentProviderContext<StripeCheckoutSettings> ctx)
        {
            var metaData = new Dictionary<string, string>
            {
                { "orderReference", ctx.Order.GenerateOrderReference() },
                { "orderId", ctx.Order.Id.ToString("D") },
                { "orderNumber", ctx.Order.OrderNumber }
            };

            if (!string.IsNullOrWhiteSpace(ctx.Settings.OrderProperties))
            {
                foreach (var alias in ctx.Settings.OrderProperties.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!string.IsNullOrWhiteSpace(ctx.Order.Properties[alias]))
                    {
                        metaData.Add(alias, ctx.Order.Properties[alias]);
                    }
                }
            }

            return metaData;
        }
    }
}

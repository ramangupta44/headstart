﻿using Headstart.Common;
using Headstart.Common.Models.Headstart;
using Headstart.Common.Services;
using Headstart.Tests.Mocks;
using NSubstitute;
using NUnit.Framework;
using ordercloud.integrations.cardconnect;
using ordercloud.integrations.exchangerates;
using OrderCloud.Catalyst;
using OrderCloud.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Headstart.Tests
{
	public class CreditCardCommandTests
	{
		private IOrderCloudIntegrationsCardConnectService _cardConnect;
		private IOrderCloudClient _oc;
		private IHsExchangeRatesService _hsExchangeRates;
		private ISupportAlertService _supportAlerts;
		private AppSettings _settings;
		private ICreditCardCommand _sut;

		private string _validRetRef = "myretref";
		private CurrencySymbol _currency = CurrencySymbol.CAD;
		private string _merchantId = "123";
		private string _cvv = "112";
		private string _orderId = "mockOrderID";
		private string _userToken = "mockUserToken";
		private string _creditCardId = "mockCreditCardID";
		private string _ccToken = "mockCcToken";
		private decimal _ccTotal = 38;
		private string _paymentId = "mockPayment";
		private string _transactionId = "trans1";

		[SetUp]
		public void Setup()
		{
			_cardConnect = Substitute.For<IOrderCloudIntegrationsCardConnectService>();
			_cardConnect.VoidAuthorization(Arg.Is<CardConnectVoidRequest>(r => r.merchid == _merchantId))
				.Returns(Task.FromResult(new CardConnectVoidResponse { }));
			_cardConnect.AuthWithoutCapture(Arg.Any<CardConnectAuthorizationRequest>())
				.Returns(Task.FromResult(new CardConnectAuthorizationResponse { authcode = "REVERS" }));

			_oc = Substitute.For<IOrderCloudClient>();
			_oc.Me.GetCreditCardAsync<CardConnectBuyerCreditCard>(_creditCardId, _userToken)
				.Returns(MockCreditCard());
			_oc.IntegrationEvents.GetWorksheetAsync<HsOrderWorksheet>(OrderDirection.Incoming, _orderId)
				.Returns(Task.FromResult(new HsOrderWorksheet { Order = new HsOrder { ID = _orderId, Total = 38 } }));
			_oc.Payments.CreateTransactionAsync<HsPayment>(OrderDirection.Incoming, _orderId, Arg.Any<string>(), Arg.Any<PaymentTransaction>())
				.Returns(Task.FromResult(new HsPayment { }));
			_oc.Payments.PatchAsync<HsPayment>(OrderDirection.Incoming, _orderId, Arg.Any<string>(), Arg.Any<PartialPayment>())
				.Returns(Task.FromResult(new HsPayment { }));

			_hsExchangeRates = Substitute.For<IHsExchangeRatesService>();
			_hsExchangeRates.GetCurrencyForUser(_userToken)
				.Returns(Task.FromResult(_currency));

			_supportAlerts = Substitute.For<ISupportAlertService>();
			_settings = Substitute.For<AppSettings>();
			_settings.CardConnectSettings.CadMerchantID = _merchantId;

			_sut = new CreditCardCommand(_cardConnect, _oc, _hsExchangeRates, _supportAlerts, _settings);
		}

		#region AuthorizePayment
		[Test]
		public async Task should_throw_if_no_payments()
		{
			// Arrange
			_oc.Payments.ListAsync<HsPayment>(OrderDirection.Incoming, _orderId, filters: Arg.Is<object>(f => (string)f == "Type=CreditCard"))
				.Returns(Task.FromResult(PaymentMocks.EmptyPaymentsList()));
			var payment = ValidIntegrationsPayment();

			// Act
			var ex = Assert.ThrowsAsync<CatalystBaseException>(async () => await _sut.AuthorizePayment(payment, _userToken, _merchantId));

			// Assert
			Assert.AreEqual("Payment.MissingCreditCardPayment", ex.Errors[0].ErrorCode);
		}

		[Test]
		public async Task should_skip_auth_if_payment_valid()
		{
			// If a payment has already been accepted and is equal to the order total then don't auth again
			// Arrange
			_oc.Payments.ListAsync<HsPayment>(OrderDirection.Incoming, _orderId, filters: Arg.Is<object>(f => (string)f == "Type=CreditCard"))
				.Returns(Task.FromResult(PaymentMocks.PaymentList(PaymentMocks.CCPayment("creditcardid1", 38))));
			var payment = ValidIntegrationsPayment();

			// Act
			await _sut.AuthorizePayment(payment, _userToken, _merchantId);
			await _cardConnect.DidNotReceive().AuthWithCapture(Arg.Any<CardConnectAuthorizationRequest>());
			await _oc.Payments.DidNotReceive().CreateTransactionAsync(OrderDirection.Incoming, _orderId, Arg.Any<string>(), Arg.Any<PaymentTransaction>());
		}

		[Test]
		public async Task should_void_if_accepted_but_not_valid()
		{
			// If a payment is accepted but doesn't match order total than we need to void before authorizing again for new amount
			var paymentTotal = 30; // credit card total is 38

			// Arrange
			var payment1transactions = new List<HsPaymentTransaction>()
			{
				new HsPaymentTransaction
				{
					ID = _transactionId,
					Succeeded = true,
					Type = "CreditCard",
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = _validRetRef
						}
					}
				}
			};
			_oc.Payments.ListAsync<HsPayment>(OrderDirection.Incoming, _orderId, filters: Arg.Is<object>(f => (string)f == "Type=CreditCard"))
				.Returns(PaymentMocks.PaymentList(MockCCPayment(paymentTotal, true, payment1transactions)));
			var payment = ValidIntegrationsPayment();

			// Act
			await _sut.AuthorizePayment(payment, _userToken, _merchantId);
			await _cardConnect.Received().VoidAuthorization(Arg.Is<CardConnectVoidRequest>(x =>
				x.retref == _validRetRef && x.merchid == _merchantId && x.currency == "CAD"));
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId, 
				_paymentId, Arg.Is<PaymentTransaction>(x => 
					x.Amount == paymentTotal));
			await _cardConnect.Received().AuthWithoutCapture(Arg.Any<CardConnectAuthorizationRequest>());
			await _oc.Payments.Received().PatchAsync<HsPayment>(OrderDirection.Incoming, _orderId, _paymentId, 
				Arg.Is<PartialPayment>(p => p.Accepted == true && p.Amount == _ccTotal));
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId, _paymentId, 
				Arg.Any<PaymentTransaction>());
		}

		[Test]
		public async Task should_handle_existing_voids()
		{
			// In a scenario where a void has already been processed on the payment, we want to make sure to
			// only try to void the last successful transaction of type "CreditCard"
			var paymentTotal = 30;

			// Arrange
			var payment1transactions = new List<HsPaymentTransaction>()
			{
				new HsPaymentTransaction
				{
					ID = "authattempt1",
					Succeeded = true,
					Type = "CreditCard",
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = "retref1"
						}
					}
				},
				new HsPaymentTransaction
				{
					ID = "voidattempt1",
					Succeeded = true,
					Type = "CreditCardVoidAuthorization",
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = "retref2"
						}
					}
				},
				new HsPaymentTransaction
				{
					ID = "authattempt2",
					Type = "CreditCard",
					Succeeded = true,
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = "retref3"
						}
					}
				}
			};
			_oc.Payments.ListAsync<HsPayment>(OrderDirection.Incoming, _orderId, filters: Arg.Is<object>(f => (string)f == "Type=CreditCard"))
				.Returns(PaymentMocks.PaymentList(MockCCPayment(paymentTotal, true, payment1transactions)));
			var payment = ValidIntegrationsPayment();

			// Act
			await _sut.AuthorizePayment(payment, _userToken, _merchantId);
			await _cardConnect.Received().VoidAuthorization(Arg.Is<CardConnectVoidRequest>(x =>
				x.retref == "retref3" && x.merchid == _merchantId && x.currency == "CAD"));
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId,_paymentId, 
				Arg.Is<PaymentTransaction>(x => x.Amount == paymentTotal));
			await _cardConnect.Received().AuthWithoutCapture(Arg.Any<CardConnectAuthorizationRequest>());
			await _oc.Payments.Received().PatchAsync<HsPayment>(OrderDirection.Incoming, _orderId, _paymentId, 
				Arg.Is<PartialPayment>(p => p.Accepted == true && p.Amount == _ccTotal));
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId, _paymentId, 
				Arg.Any<PaymentTransaction>());
		}

		[Test]
		public async Task should_handle_existing_voids_us()
		{
			// Same as should_handle_existing_voids but handle usd merchant
			var paymentTotal = 30;

			// Arrange
			var payment1transactions = new List<HsPaymentTransaction>()
			{
				new HsPaymentTransaction
				{
					ID = "authattempt1",
					Succeeded = true,
					Type = "CreditCard",
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = "retref1"
						}
					}
				},
				new HsPaymentTransaction
				{
					ID = "voidattempt1",
					Succeeded = true,
					Type = "CreditCardVoidAuthorization",
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = "retref2"
						}
					}
				},
				new HsPaymentTransaction
				{
					ID = "authattempt2",
					Type = "CreditCard",
					Succeeded = true,
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = "retref3"
						}
					}
				}
			};
			_settings.CardConnectSettings.UsdMerchantID = _merchantId;
			_settings.CardConnectSettings.CadMerchantID = "somethingelse";
			_hsExchangeRates.GetCurrencyForUser(_userToken)
				.Returns(Task.FromResult(CurrencySymbol.USD));
			_oc.Payments.ListAsync<HsPayment>(OrderDirection.Incoming, _orderId, filters: Arg.Is<object>(f => (string)f == "Type=CreditCard"))
				.Returns(PaymentMocks.PaymentList(MockCCPayment(paymentTotal, true, payment1transactions)));
			var payment = ValidIntegrationsPayment();

			// Act
			await _sut.AuthorizePayment(payment, _userToken, _merchantId);
			await _cardConnect.Received().VoidAuthorization(Arg.Is<CardConnectVoidRequest>(x =>
				x.retref == "retref3" && x.merchid == _merchantId && x.currency == "USD"));
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId, _paymentId, 
				Arg.Is<PaymentTransaction>(x => x.Amount == paymentTotal));
			await _cardConnect.Received().AuthWithoutCapture(Arg.Any<CardConnectAuthorizationRequest>());
			await _oc.Payments.Received().PatchAsync<HsPayment>(OrderDirection.Incoming, _orderId, _paymentId, 
				Arg.Is<PartialPayment>(p => p.Accepted == true && p.Amount == _ccTotal));
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId, _paymentId, 
				Arg.Any<PaymentTransaction>());
		}

		[Test]
		public async Task should_still_create_transaction_on_failed_auths()
		{
			// This gives us full insight into transaction history, as well as sets Accepted to false
			var paymentTotal = 38;

			// Arrange
			var payment1transactions = new List<HsPaymentTransaction>()
			{
				new HsPaymentTransaction
				{
					ID = _transactionId,
					Succeeded = true,
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = _validRetRef
						}
					}
				}
			};
			var mockedCCPayment = MockCCPayment(paymentTotal, false, payment1transactions);
			_oc.Payments.ListAsync<HsPayment>(OrderDirection.Incoming, _orderId, filters: Arg.Is<object>(f => (string)f == "Type=CreditCard"))
				.Returns(PaymentMocks.PaymentList(mockedCCPayment));
			_oc.Payments.PatchAsync<HsPayment>(OrderDirection.Incoming, _orderId, Arg.Any<string>(), Arg.Any<PartialPayment>())
				.Returns(Task.FromResult(mockedCCPayment));
			var payment = ValidIntegrationsPayment();
			_cardConnect
				.When(x => x.AuthWithoutCapture(Arg.Any<CardConnectAuthorizationRequest>()))
				.Do(x => throw new CreditCardAuthorizationException(new ApiError(), new CardConnectAuthorizationResponse()));

			// Act
			var ex = Assert.ThrowsAsync<CatalystBaseException>(async () => await _sut.AuthorizePayment(payment, _userToken, _merchantId));

			// Assert
			Assert.AreEqual("CreditCardAuth.", ex.Errors[0].ErrorCode);
			await _cardConnect.Received().AuthWithoutCapture(Arg.Any<CardConnectAuthorizationRequest>());

			// Stuff that happens in catch block
			await _oc.Payments.Received().PatchAsync<HsPayment>(OrderDirection.Incoming, _orderId, _paymentId,
				Arg.Is<PartialPayment>(p => p.Accepted == false && p.Amount == _ccTotal));
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId, _paymentId,
				Arg.Any<PaymentTransaction>());
		}

		[Test]
		public async Task should_handle_failed_auth_void()
		{
			// Creates a new transaction when auth fails which gives us full insight into
			// transaction history as well as sets Accepted to false
			var paymentTotal = 30;

			// Arrange
			var payment1Transactions = new List<HsPaymentTransaction>()
			{
				new HsPaymentTransaction
				{
					ID = _transactionId,
					Succeeded = true,
					Type = "CreditCard",
					xp = new TransactionXP
					{
						CardConnectResponse = new CardConnectAuthorizationResponse
						{
							retref = _validRetRef
						}
					}
				}
			};
			var mockedCCPayment = MockCCPayment(paymentTotal, true, payment1Transactions);
			_oc.Payments.ListAsync<HsPayment>(OrderDirection.Incoming, _orderId, filters: Arg.Is<object>(f => (string)f == "Type=CreditCard"))
				.Returns(PaymentMocks.PaymentList(mockedCCPayment));
			var payment = ValidIntegrationsPayment();
			_cardConnect
				.When(x => x.VoidAuthorization(Arg.Any<CardConnectVoidRequest>()))
				.Do(x => throw new CreditCardVoidException(new ApiError(), new CardConnectVoidResponse()));

			// Act
			var ex = Assert.ThrowsAsync<CatalystBaseException>(async () => await _sut.AuthorizePayment(payment, _userToken, _merchantId));

			// Assert
			Assert.AreEqual("Payment.FailedToVoidAuthorization", ex.Errors[0].ErrorCode);

			// Stuff that happens in catch block
			await _supportAlerts
				.Received().VoidAuthorizationFailed(Arg.Any<HsPayment>(), _transactionId,
					Arg.Any<HsOrder>(), Arg.Any<CreditCardVoidException>());
			await _oc.Payments.Received().CreateTransactionAsync(OrderDirection.Incoming, _orderId, _paymentId,
				Arg.Any<PaymentTransaction>());
		}
		#endregion AuthorizePayment

		private HsPayment MockCCPayment(decimal amount, bool accepted = false, List<HsPaymentTransaction> transactions = null)
		{
			return new HsPayment
			{
				ID = _paymentId,
				Type = PaymentType.CreditCard,
				Amount = amount,
				Accepted = accepted,
				xp = new PaymentXp(),
				Transactions = new ReadOnlyCollection<HsPaymentTransaction>(transactions ?? new List<HsPaymentTransaction>())
			};
		}

		private Task<CardConnectBuyerCreditCard> MockCreditCard()
		{
			return Task.FromResult(new CardConnectBuyerCreditCard
			{
				Token = _ccToken,
				ExpirationDate = new DateTimeOffset(),
				xp = new CreditCardXP
				{
					CCBillingAddress = new Address {}
				}
			});
		}

		private OrderCloudIntegrationsCreditCardPayment ValidIntegrationsPayment()
		{
			return new OrderCloudIntegrationsCreditCardPayment
			{
				CVV = _cvv,
				CreditCardID = _creditCardId,
				OrderID = _orderId
			};
		}
	}
}
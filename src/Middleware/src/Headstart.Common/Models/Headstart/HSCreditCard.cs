﻿using OrderCloud.SDK;

namespace Headstart.Common.Models.Headstart
{
	public class HSCreditCard : CreditCard<HSCreditCardXP>
	{
	}

	public class HSBuyerCreditCard : BuyerCreditCard<HSCreditCardXP>
	{
	}

	public class HSCreditCardXP
	{
		[Required] 
		public Address CCBillingAddress { get; set; } = new Address();
	}
}
﻿using Headstart.Common.Models.Headstart;
using Headstart.Common.Repositories;
using Headstart.Common.Repositories.Models;
using OrderCloud.Catalyst;
using OrderCloud.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Headstart.Jobs
{
	public class ReceiveRecentLineItemsJob : BaseReportJob
	{
		private readonly IOrderCloudClient _oc;
		private readonly ILineItemDetailDataRepo _lineItemDetailDataRepo;

		public ReceiveRecentLineItemsJob(IOrderCloudClient oc, ILineItemDetailDataRepo lineItemDetailDataRepo)
		{
			_oc = oc;
			_lineItemDetailDataRepo = lineItemDetailDataRepo;
		}

		protected override async Task<ResultCode> ProcessJobAsync(string message)
		{
			try
			{
				await UpsertLineItemDetail(message);
				return ResultCode.Success;
			}
			catch (Exception ex)
			{
				LogFailure($@"{ex.Message} {ex?.InnerException?.Message} {ex.StackTrace}");
				return ResultCode.PermanentFailure;
			}
		}

		private async Task UpsertLineItemDetail(string orderID)
		{
			var orderWorksheet = await _oc.IntegrationEvents.GetWorksheetAsync<HsOrderWorksheet>(OrderDirection.Incoming, orderID);

			var lineItems = await _oc.LineItems.ListAllAsync<HsLineItem>(OrderDirection.Incoming, orderID);

			var buyer = await _oc.Buyers.GetAsync<HsBuyer>(orderWorksheet.Order.FromCompanyID);

			var lineItemsWithMiscFields = await BuildLineItemsMiscFields(lineItems, orderWorksheet, buyer.Name);

			var lineItemsWithPurchaseOrders = await BuildLineItemsWithPurchaseOrders(orderID);

			var orderLineItemData = new HsOrderLineItemData()
			{ 
				Order = orderWorksheet.Order,
				LineItems = lineItems,
				LineItemsWithMiscFields = lineItemsWithMiscFields,
				LineItemsWithPurchaseOrderFields = lineItemsWithPurchaseOrders
			};

			var queryable = _lineItemDetailDataRepo.GetQueryable().Where(order => order.PartitionKey == @"PartitionValue");

			var requestOptions = BuildQueryRequestOptions();

			var cosmosLineItemOrder = new LineItemDetailData()
			{ 
				PartitionKey = @"PartitionValue",
				OrderId = orderID,
				Data = orderLineItemData
			};

			var listOptions = BuildListOptions(orderID);

			var currentLineItemListPage = await _lineItemDetailDataRepo.GetItemsAsync(queryable, requestOptions, listOptions);
			var cosmosId = string.Empty;
			if (currentLineItemListPage.Items.Count() == 1)
			{
				cosmosId = cosmosLineItemOrder.id = currentLineItemListPage.Items[0].id;
			}

			await _lineItemDetailDataRepo.UpsertItemAsync(cosmosId, cosmosLineItemOrder);
		}

		private async Task<List<LineItemsWithPurchaseOrderFields>> BuildLineItemsWithPurchaseOrders(string orderID)
		{
			//returns POs
			var orders = await _oc.Orders.ListAllAsync<HsOrder>(OrderDirection.Outgoing, filters: $@"ID={orderID}-*");

			//loop through orders, get line items, pass those.
			var orderLineItemBySupplierId = await GetLineItemsFromPurchaseOrdersAsync(orders);
			return orderLineItemBySupplierId;
		}

		private async Task<List<LineItemsWithPurchaseOrderFields>> GetLineItemsFromPurchaseOrdersAsync(List<HsOrder> orders)
		{
			var result = new List<LineItemsWithPurchaseOrderFields>() { };

			foreach (var order in orders)
			{
				var lineItemsBySupplier = await _oc.LineItems.ListAllAsync<HsLineItem>(OrderDirection.Outgoing, order.ID);
				if (lineItemsBySupplier.Count() <= 0)
				{
					continue;
				}

				foreach (var lineItem in lineItemsBySupplier)
				{
					var lineItemWithPurchaseOrder = new LineItemsWithPurchaseOrderFields
					{
						Id = lineItem.ID,
						OrderId = order.ID,
						Subtotal = order.Subtotal,
						Total = order.Total,
						UnitPrice = lineItem.UnitPrice,
						SupplierId = lineItem.SupplierID
					};
					result.Add(lineItemWithPurchaseOrder);
				}
			}
			return result;
		}

		private async Task<List<LineItemMiscReportFields>> BuildLineItemsMiscFields(List<HsLineItem> lineItems, HsOrderWorksheet orderWorksheet, string buyerName)
		{
			var lineItemsWithMiscFields = new List<LineItemMiscReportFields>();

			foreach (var lineItem in lineItems)
			{
				var lineItemSupplier = await _oc.Suppliers.GetAsync<HsSupplier>(lineItem.SupplierID);
				var lineItemWithMiscFields = new LineItemMiscReportFields
				{
					Id = lineItem.ID,
					SupplierName = lineItemSupplier?.Name,
					BrandName = buyerName
				};

				if (orderWorksheet.OrderCalculateResponse != null && orderWorksheet.OrderCalculateResponse.xp != null && orderWorksheet.OrderCalculateResponse.xp.TaxCalculation.ExternalTransactionID != "NotTaxable")
				{
					var lineTax = orderWorksheet.OrderCalculateResponse.xp.TaxCalculation.LineItems.FirstOrDefault(line => line.LineItemID == lineItem.ID);
					lineItemWithMiscFields.Tax = lineTax?.LineItemTotalTax;
					lineItemWithMiscFields.LineTaxAvailable = lineTax != null;
				}
				else
				{
					lineItemWithMiscFields.Tax = null;
					lineItemWithMiscFields.LineTaxAvailable = false;
				}
				lineItemsWithMiscFields.Add(lineItemWithMiscFields);
			}
			return lineItemsWithMiscFields;
		}
	}
}
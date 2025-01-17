using System;
using System.Linq;
using OrderCloud.SDK;
using System.Dynamic;
using Headstart.Common;
using OrderCloud.Catalyst;
using Sitecore.Diagnostics;
using System.Threading.Tasks;
using Headstart.Common.Helpers;
using System.Collections.Generic;
using Headstart.Common.Services.CMS;
using Headstart.Common.Models.Headstart;
using ordercloud.integrations.library.Cosmos;
using Sitecore.Foundation.SitecoreExtensions.Extensions;

namespace Headstart.API.Commands
{
	public interface IHsProductCommand
	{
		Task<SuperHsProduct> Get(string id, string token);
		Task<ListPage<SuperHsProduct>> List(ListArgs<HsProduct> args, string token);
		Task<SuperHsProduct> Post(SuperHsProduct product, DecodedToken decodedToken);
		Task<SuperHsProduct> Put(string id, SuperHsProduct product, string token);
		Task Delete(string id, string token);
		Task<HsPriceSchedule> GetPricingOverride(string id, string buyerId, string token);
		Task DeletePricingOverride(string id, string buyerId, string token);
		Task<HsPriceSchedule> UpdatePricingOverride(string id, string buyerId, HsPriceSchedule pricingOverride, string token);
		Task<HsPriceSchedule> CreatePricingOverride(string id, string buyerId, HsPriceSchedule pricingOverride, string token);
		Task<Product> FilterOptionOverride(string id, string supplierId, IDictionary<string, object> facets, DecodedToken decodedToken);
	}

	public class DefaultOptionSpecAssignment
	{
		public string SpecID { get; set; }
		public string OptionID { get; set; }
	}


	public class HsProductCommand : IHsProductCommand
	{
		private readonly IOrderCloudClient _oc;
		private readonly AppSettings _settings;
		private readonly ISupplierApiClientHelper _apiClientHelper;
		private readonly IAssetClient _assetClient;

		/// <summary>
		/// The IOC based constructor method for the HsProductCommand class object with Dependency Injection
		/// </summary>
		/// <param name="settings"></param>
		/// <param name="elevatedOc"></param>
		/// <param name="apiClientHelper"></param>
		/// <param name="assetClient"></param>
		public HsProductCommand(AppSettings settings, IOrderCloudClient elevatedOc, ISupplierApiClientHelper apiClientHelper, IAssetClient assetClient)
		{			
			try
			{
				_oc = elevatedOc;
				_settings = settings;
				_apiClientHelper = apiClientHelper;
				_assetClient = assetClient;
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
		}

		/// <summary>
		/// Public re-usable GetPricingOverride task method
		/// </summary>
		/// <param name="id"></param>
		/// <param name="buyerId"></param>
		/// <param name="token"></param>
		/// <returns>The HsPriceSchedule response object from the GetPricingOverride process</returns>
		public async Task<HsPriceSchedule> GetPricingOverride(string id, string buyerId, string token)
		{
			var priceSchedule = new HsPriceSchedule();
			try
			{
				var priceScheduleID = $@"{id}-{buyerId}";
				priceSchedule = await _oc.PriceSchedules.GetAsync<HsPriceSchedule>(priceScheduleID);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return priceSchedule;
		}

		/// <summary>
		/// Public re-usable DeletePricingOverride task method
		/// </summary>
		/// <param name="id"></param>
		/// <param name="buyerId"></param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task DeletePricingOverride(string id, string buyerId, string token)
		{
			try
			{
				/* must remove the price schedule from the visibility assignments
				* deleting a price schedule with active visibility assignments removes the visbility
				* assignment completely, we want those product to usergroup catalog assignments to remain
				* just without the override */
				var priceScheduleID = $@"{id}-{buyerId}";
				await RemovePriceScheduleAssignmentFromProductCatalogAssignments(id, buyerId, priceScheduleID);
				await _oc.PriceSchedules.DeleteAsync(priceScheduleID);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
		}

		/// <summary>
		/// Public re-usable CreatePricingOverride task method
		/// </summary>
		/// <param name="id"></param>
		/// <param name="buyerId"></param>
		/// <param name="priceSchedule"></param>
		/// <param name="token"></param>
		/// <returns>The HsPriceSchedule response object from the CreatePricingOverride process</returns>
		public async Task<HsPriceSchedule> CreatePricingOverride(string id, string buyerId, HsPriceSchedule priceSchedule, string token)
		{
			var newPriceSchedule = new HsPriceSchedule();
			try
			{
				/* must add the price schedule to the visibility assignments */
				var priceScheduleID = $@"{id}-{buyerId}";
				priceSchedule.ID = priceScheduleID;
				newPriceSchedule = await _oc.PriceSchedules.SaveAsync<HsPriceSchedule>(priceScheduleID, priceSchedule);
				await AddPriceScheduleAssignmentToProductCatalogAssignments(id, buyerId, priceScheduleID);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return newPriceSchedule;
		}

		/// <summary>
		/// Public re-usable UpdatePricingOverride task method
		/// </summary>
		/// <param name="id"></param>
		/// <param name="buyerId"></param>
		/// <param name="priceSchedule"></param>
		/// <param name="token"></param>
		/// <returns>The HsPriceSchedule response object from the UpdatePricingOverride process</returns>
		public async Task<HsPriceSchedule> UpdatePricingOverride(string id, string buyerId, HsPriceSchedule priceSchedule, string token)
		{
			var newPriceSchedule = new HsPriceSchedule();
			try
			{
				var priceScheduleID = $@"{id}-{buyerId}";
				newPriceSchedule = await _oc.PriceSchedules.SaveAsync<HsPriceSchedule>(priceScheduleID, priceSchedule);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return newPriceSchedule;
		}

		/// <summary>
		/// Public re-usable RemovePriceScheduleAssignmentFromProductCatalogAssignments task method
		/// </summary>
		/// <param name="productId"></param>
		/// <param name="buyerId"></param>
		/// <param name="priceScheduleId"></param>
		/// <returns></returns>
		public async Task RemovePriceScheduleAssignmentFromProductCatalogAssignments(string productId, string buyerId, string priceScheduleId)
		{			
			try
			{
				var relatedProductCatalogAssignments = await _oc.Products.ListAssignmentsAsync(productID: productId, buyerID: buyerId, pageSize: 100);
				await Throttler.RunAsync(relatedProductCatalogAssignments.Items, 100, 5, assignment =>
				{
					return _oc.Products.SaveAssignmentAsync(new ProductAssignment
					{
						BuyerID = buyerId,
						PriceScheduleID = null,
						ProductID = productId,
						UserGroupID = assignment.UserGroupID
					});
				});
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
		}

		/// <summary>
		/// Public re-usable AddPriceScheduleAssignmentToProductCatalogAssignments task method
		/// </summary>
		/// <param name="productId"></param>
		/// <param name="buyerId"></param>
		/// <param name="priceScheduleId"></param>
		/// <returns></returns>
		public async Task AddPriceScheduleAssignmentToProductCatalogAssignments(string productId, string buyerId, string priceScheduleId)
		{
			try
			{
				var relatedProductCatalogAssignments = await _oc.Products.ListAssignmentsAsync(productID: productId, buyerID: buyerId, pageSize: 100);
				await Throttler.RunAsync(relatedProductCatalogAssignments.Items, 100, 5, assignment =>
				{
					return _oc.Products.SaveAssignmentAsync(new ProductAssignment
					{
						BuyerID = buyerId,
						PriceScheduleID = priceScheduleId,
						ProductID = productId,
						UserGroupID = assignment.UserGroupID
					});
				});
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
		}

		/// <summary>
		/// Public re-usable Get SuperHsProduct task method
		/// </summary>
		/// <param name="id"></param>
		/// <param name="token"></param>
		/// <returns>The SuperHsProduct response object from the Get SuperHsProduct process</returns>
		public async Task<SuperHsProduct> Get(string id, string token)
		{			
			var resp = new SuperHsProduct();
			try
			{
				var _product = await _oc.Products.GetAsync<HsProduct>(id, token);
				var _priceSchedule = await _oc.PriceSchedules.GetAsync<PriceSchedule>(_product.ID, token);
				var _specs = _oc.Products.ListSpecsAsync(id, null, null, null, 1, 100, null, token);
				var _variants = _oc.Products.ListVariantsAsync<HsVariant>(id, null, null, null, 1, 100, null, token);
				resp = new SuperHsProduct
				{
					Product = _product,
					PriceSchedule = _priceSchedule,
					Specs = (await _specs).Items,
					Variants = (await _variants).Items,
				};
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Public re-usable get a list of ListPage of SuperHsProduct response objects task method
		/// </summary>
		/// <param name="args"></param>
		/// <param name="token"></param>
		/// <returns>The ListPage of SuperHsProduct response objects</returns>
		public async Task<ListPage<SuperHsProduct>> List(ListArgs<HsProduct> args, string token)
		{
			var resp = new ListPage<SuperHsProduct>();
			try
			{
				var filterString = args.ToFilterString();
				var productsList = await _oc.Products.ListAsync<HsProduct>(filters: string.IsNullOrEmpty(filterString) ? null : filterString, search: args.Search, searchType: SearchType.ExactPhrasePrefix,
					sortBy: args.SortBy.FirstOrDefault(), pageSize: args.PageSize, page: args.Page, accessToken: token);
				var superProductsList = new List<SuperHsProduct> { };
				await Throttler.RunAsync(productsList.Items, 100, 10, async product =>
				{
					var priceSchedule = _oc.PriceSchedules.GetAsync(product.DefaultPriceScheduleID, token);
					var _specs = _oc.Products.ListSpecsAsync(product.ID, null, null, null, 1, 100, null, token);
					var _variants = _oc.Products.ListVariantsAsync<HsVariant>(product.ID, null, null, null, 1, 100, null, token);
					superProductsList.Add(new SuperHsProduct
					{
						Product = product,
						PriceSchedule = await priceSchedule,
						Specs = (await _specs).Items,
						Variants = (await _variants).Items,
					});
				});
				resp = new ListPage<SuperHsProduct>
				{
					Meta = productsList.Meta,
					Items = superProductsList
				};
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Public re-usable Get SuperHsProduct task method
		/// </summary>
		/// <param name="superProduct"></param>
		/// <param name="decodedToken"></param>
		/// <returns>The SuperHsProduct response object from the SaveAsync processes</returns>
		/// <exception cref="Exception"></exception>
		public async Task<SuperHsProduct> Post(SuperHsProduct superProduct, DecodedToken decodedToken)
		{
			var resp = new SuperHsProduct();
			try
			{
				// Determine ID up front so price schedule ID can match
				superProduct.Product.ID = superProduct.Product.ID ?? CosmosInteropID.New();
				await ValidateVariantsAsync(superProduct, decodedToken.AccessToken);
				// Create Specs
				var defaultSpecOptions = new List<DefaultOptionSpecAssignment>();
				var specRequests = await Throttler.RunAsync(superProduct.Specs, 100, 5, s =>
				{
					defaultSpecOptions.Add(new DefaultOptionSpecAssignment { SpecID = s.ID, OptionID = s.DefaultOptionID });
					s.DefaultOptionID = null;
					return _oc.Specs.SaveAsync<Spec>(s.ID, s, accessToken: decodedToken.AccessToken);
				});

				// Create Spec Options
				foreach (Spec spec in superProduct.Specs)
				{
					await Throttler.RunAsync(spec.Options, 100, 5, o => _oc.Specs.SaveOptionAsync(spec.ID, o.ID, o, accessToken: decodedToken.AccessToken));
				}

				// Patch Specs with requested DefaultOptionID
				await Throttler.RunAsync(defaultSpecOptions, 100, 10, a => _oc.Specs.PatchAsync(a.SpecID, new PartialSpec { DefaultOptionID = a.OptionID }, accessToken: decodedToken.AccessToken));
				// Create Price Schedule
				PriceSchedule _priceSchedule = null;
				//All products must have a price schedule for orders to be submitted.  The front end provides a default Price of $0 for quote products that don't have one.
				superProduct.PriceSchedule.ID = superProduct.Product.ID;
				try
				{
					_priceSchedule = await _oc.PriceSchedules.CreateAsync<PriceSchedule>(superProduct.PriceSchedule, decodedToken.AccessToken);
				}
				catch (OrderCloudException ex)
				{
					if (ex.HttpStatus == System.Net.HttpStatusCode.Conflict)
					{
						var exception =  $@"The Product SKU {superProduct.PriceSchedule.ID} already exists. Please try a different SKU. {ex.Message}.";
						LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", exception, ex.StackTrace, this, true);
					}
				}
				superProduct.Product.DefaultPriceScheduleID = _priceSchedule.ID;
				// Create Product
				if (decodedToken.CommerceRole == CommerceRole.Supplier)
				{
					var me = await _oc.Me.GetAsync(accessToken: decodedToken.AccessToken);
					var supplierName = await GetSupplierNameForXpFacet(me.Supplier.ID, decodedToken.AccessToken);
					superProduct.Product.xp.Facets.Add(@"supplier", new List<string>() { supplierName });
				}

				var _product = await _oc.Products.CreateAsync<HsProduct>(superProduct.Product, decodedToken.AccessToken);
				// Make Spec Product Assignments
				await Throttler.RunAsync(superProduct.Specs, 100, 5, s => _oc.Specs.SaveProductAssignmentAsync(new SpecProductAssignment
				{
					ProductID = _product.ID, 
					SpecID = s.ID
				}, accessToken: decodedToken.AccessToken));
				// Generate Variants
				await _oc.Products.GenerateVariantsAsync(_product.ID, accessToken: decodedToken.AccessToken);
				// Patch Variants with the User Specified ID(SKU) AND necessary display xp values
				await Throttler.RunAsync(superProduct.Variants, 100, 5, v =>
				{
					var oldVariantID = v.ID;
					v.ID = v.xp.NewId ?? v.ID;
					v.Name = v.xp.NewId ?? v.ID;

					if ((superProduct?.Product?.Inventory?.VariantLevelTracking) == true && v.Inventory == null)
					{
						v.Inventory = new PartialVariantInventory { QuantityAvailable = 0 };
					}
					if (superProduct.Product?.Inventory == null)
					{
						//If Inventory doesn't exist on the product, don't patch variants with inventory either.
						return _oc.Products.PatchVariantAsync(_product.ID, oldVariantID, new PartialVariant { ID = v.ID, Name = v.Name, xp = v.xp }, accessToken: decodedToken.AccessToken);
					}
					else
					{
						return _oc.Products.PatchVariantAsync(_product.ID, oldVariantID, new PartialVariant { ID = v.ID, Name = v.Name, xp = v.xp, Inventory = v.Inventory }, accessToken: decodedToken.AccessToken);
					}
				});

				// List Variants
				var _variants = await _oc.Products.ListVariantsAsync<HsVariant>(_product.ID, accessToken: decodedToken.AccessToken);
				// List Product Specs
				var _specs = await _oc.Products.ListSpecsAsync<Spec>(_product.ID, accessToken: decodedToken.AccessToken);
				// Return the SuperProduct
				resp = new SuperHsProduct
				{
					Product = _product,
					PriceSchedule = _priceSchedule,
					Specs = _specs.Items,
					Variants = _variants.Items,
				};
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Private re-usable Get SuperHsProduct task method
		/// </summary>
		/// <param name="superProduct"></param>
		/// <param name="token"></param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		private async Task ValidateVariantsAsync(SuperHsProduct superProduct, string token)
        {
			try
			{
				List<Variant> allVariants = new List<Variant>();
				if (superProduct.Variants == null || !superProduct.Variants.Any()) 
				{ 
					return; 
				}

				var allProducts = await _oc.Products.ListAllAsync(accessToken: token);
				if (allProducts == null || !allProducts.Any()) 
				{ 
					return; 
				}

				foreach (Product product in allProducts)
				{
					if (product.VariantCount > 0 && product.ID != superProduct.Product.ID)
					{
						allVariants.AddRange((await _oc.Products.ListVariantsAsync(productID: product.ID, pageSize: 100, accessToken: token)).Items);
					}
				}

				foreach (Variant variant in superProduct.Variants)
				{
					if (!allVariants.Any()) 
					{ 
						return; 
					}

					List<Variant> duplicateSpecNames = allVariants.Where(currVariant => IsDifferentVariantWitHsameName(variant, currVariant)).ToList();
					if (duplicateSpecNames.Any())
					{
						var exception = $@"{duplicateSpecNames.First().ID} already exists on a variant. Please use unique names for SKUS and try again.";
						LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", exception, "", this, true);
					}
				}
			} 
			catch (Exception ex)
            {
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
            }
        }

		/// <summary>
		/// Private re-usable IsDifferentVariantWitHsameName method
		/// </summary>
		/// <param name="variant"></param>
		/// <param name="currVariant"></param>
		/// <returns>The boolean status for the IsDifferentVariantWitHsameName process</returns>
		private bool IsDifferentVariantWitHsameName(Variant variant, Variant currVariant)
		{
			var resp = false;
			try
			{
				//Do they have the same SKU
				if (variant.xp.NewID == currVariant.ID)
				{
					if (variant.xp.SpecCombo == currVariant.xp.SpecCombo)
					{
						//It's most likely the same variant
						return false;
					}
					resp = true;
				}
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Public re-usable Get SuperHsProduct task method for the Products.SaveAsync process
		/// </summary>
		/// <param name="id"></param>
		/// <param name="superProduct"></param>
		/// <param name="token"></param>
		/// <returns>The SuperHsProduct response object from the Products.SaveAsync process</returns>
		public async Task<SuperHsProduct> Put(string id, SuperHsProduct superProduct, string token)
		{
			var resp = new SuperHsProduct();
			try
			{
				// Update the Product itself
				var _updatedProduct = await _oc.Products.SaveAsync<HsProduct>(superProduct.Product.ID, superProduct.Product, token);
				// Two spec lists to compare (requestSpecs and existingSpecs)
				IList<Spec> requestSpecs = superProduct.Specs.ToList();
				IList<Spec> existingSpecs = (await _oc.Products.ListSpecsAsync(id, accessToken: token)).Items.ToList();
				// Two variant lists to compare (requestVariants and existingVariants)
				IList<HsVariant> requestVariants = superProduct.Variants;
				IList<Variant> existingVariants = (await _oc.Products.ListVariantsAsync(id, pageSize: 100, accessToken: token)).Items.ToList();
				// Calculate differences in specs - specs to add, and specs to delete
				var specsToAdd = requestSpecs.Where(s => !existingSpecs.Any(s2 => s2.ID == s.ID)).ToList();
				var specsToDelete = existingSpecs.Where(s => !requestSpecs.Any(s2 => s2.ID == s.ID)).ToList();
				// Get spec options to add -- WAIT ON THESE, RUN PARALLEL THE ADD AND DELETE SPEC REQUESTS
				foreach (var rSpec in requestSpecs)
				{
					foreach (var eSpec in existingSpecs)
					{
						if (rSpec.ID == eSpec.ID)
						{
							await Throttler.RunAsync(rSpec.Options.Where(rso => !eSpec.Options.Any(eso => eso.ID == rso.ID)), 100, 5, o => _oc.Specs.CreateOptionAsync(rSpec.ID, o, accessToken: token));
							await Throttler.RunAsync(eSpec.Options.Where(eso => !rSpec.Options.Any(rso => rso.ID == eso.ID)), 100, 5, o => _oc.Specs.DeleteOptionAsync(rSpec.ID, o.ID, accessToken: token));
						}
					};
				};

				// Create new specs and Delete removed specs
				var defaultSpecOptions = new List<DefaultOptionSpecAssignment>();
				await Throttler.RunAsync(specsToAdd, 100, 5, s =>
				{
					defaultSpecOptions.Add(new DefaultOptionSpecAssignment { SpecID = s.ID, OptionID = s.DefaultOptionID });
					s.DefaultOptionID = null;
					return _oc.Specs.SaveAsync<Spec>(s.ID, s, accessToken: token);
				});
				await Throttler.RunAsync(specsToDelete, 100, 5, s => _oc.Specs.DeleteAsync(s.ID, accessToken: token));

				// Add spec options for new specs
				foreach (var spec in specsToAdd)
				{
					await Throttler.RunAsync(spec.Options, 100, 5, o => _oc.Specs.CreateOptionAsync(spec.ID, o, accessToken: token));
				}
				// Patch Specs with requested DefaultOptionID
				await Throttler.RunAsync(defaultSpecOptions, 100, 10, a => _oc.Specs.PatchAsync(a.SpecID, new PartialSpec { DefaultOptionID = a.OptionID }, accessToken: token));
				// Make assignments for the new specs
				await Throttler.RunAsync(specsToAdd, 100, 5, s => _oc.Specs.SaveProductAssignmentAsync(new SpecProductAssignment { ProductID = id, SpecID = s.ID }, accessToken: token));
				HandleSpecOptionChanges(requestSpecs, existingSpecs, token);
				// Check if Variants differ
				var variantsAdded = requestVariants.Any(v => !existingVariants.Any(v2 => v2.ID == v.ID));
				var variantsRemoved = existingVariants.Any(v => !requestVariants.Any(v2 => v2.ID == v.ID));
				bool hasVariantChange = false;

				foreach (Variant variant in requestVariants)
				{
					ValidateRequestVariant(variant);
					var currVariant = existingVariants.Where(v => v.ID == variant.ID);
					if (currVariant == null || currVariant.Count() < 1) 
					{ 
						continue; 
					}

					hasVariantChange = HasVariantChange(variant, currVariant.First());
					if (hasVariantChange) 
					{ 
						break; 
					}
				}

				// If variants differ, then re-generate variants and re-patch IDs to match the user input.
				if (variantsAdded || variantsRemoved || hasVariantChange || requestVariants.Any(v => v.xp.NewId != null))
				{
					//Validate variant names before continuing saving.
					await ValidateVariantsAsync(superProduct, token);
					// Re-generate Variants
					await _oc.Products.GenerateVariantsAsync(id, overwriteExisting: true, accessToken: token);
					// Patch NEW variants with the User Specified ID (Name,ID), and correct xp values (SKU)
					await Throttler.RunAsync(superProduct.Variants, 100, 5, v =>
					{
						v.ID = v.xp.NewId ?? v.ID;
						v.Name = v.xp.NewId ?? v.ID;
						if ((superProduct?.Product?.Inventory?.VariantLevelTracking) == true && v.Inventory == null)
						{
							v.Inventory = new PartialVariantInventory { QuantityAvailable = 0 };
						}
						if (superProduct.Product?.Inventory == null)
						{
							//If Inventory doesn't exist on the product, don't patch variants with inventory either.
							return _oc.Products.PatchVariantAsync(id, $"{superProduct.Product.ID}-{v.xp.SpecCombo}", new PartialVariant { ID = v.ID, Name = v.Name, xp = v.xp, Active = v.Active }, accessToken: token);
						}
						else
						{
							return _oc.Products.PatchVariantAsync(id, $"{superProduct.Product.ID}-{v.xp.SpecCombo}", new PartialVariant { ID = v.ID, Name = v.Name, xp = v.xp, Active = v.Active, Inventory = v.Inventory }, accessToken: token);
						}
					});
				};

				// If applicable, update OR create the Product PriceSchedule
				var tasks = new List<Task>();
				Task<PriceSchedule> _priceScheduleReq = null;
				if (superProduct.PriceSchedule != null)
				{
					_priceScheduleReq = UpdateRelatedPriceSchedules(superProduct.PriceSchedule, token);
					tasks.Add(_priceScheduleReq);
				}
				// List Variants
				var _variantsReq = _oc.Products.ListVariantsAsync<HsVariant>(id, pageSize: 100, accessToken: token);
				tasks.Add(_variantsReq);
				// List Product Specs
				var _specsReq = _oc.Products.ListSpecsAsync<Spec>(id, accessToken: token);
				tasks.Add(_specsReq);

				await Task.WhenAll(tasks);
				resp = new SuperHsProduct
				{
					Product = _updatedProduct,
					PriceSchedule = _priceScheduleReq?.Result,
					Specs = _specsReq?.Result?.Items,
					Variants = _variantsReq?.Result?.Items,
				};
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Private re-usable ValidateRequestVariant method
		/// </summary>
		/// <param name="variant"></param>
		private void ValidateRequestVariant(Variant variant)
		{
			try
			{
				if (variant.xp.NewID == variant.ID)
				{
					//If NewID is same as ID, no changes have happened so NewID shouldn't be populated.
					variant.xp.NewID = null;
				}
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
		}

		/// <summary>
		/// Private re-usable UpdateRelatedPriceSchedules task method
		/// </summary>
		/// <param name="updated"></param>
		/// <param name="token"></param>
		/// <returns>The PriceSchedule response object from the UpdateRelatedPriceSchedules process</returns>
		private async Task<PriceSchedule> UpdateRelatedPriceSchedules(PriceSchedule updated, string token)
		{
			var resp = new PriceSchedule();
			try
			{
				var ocAuth = await _oc.AuthenticateAsync();
				var initial = await _oc.PriceSchedules.GetAsync(updated.ID);
				if (initial.MaxQuantity != updated.MaxQuantity || initial.MinQuantity != updated.MinQuantity || initial.UseCumulativeQuantity != updated.UseCumulativeQuantity || initial.RestrictedQuantity != updated.RestrictedQuantity ||
				    initial.ApplyShipping != updated.ApplyShipping || initial.ApplyTax != updated.ApplyTax)
				{
					var patch = new PartialPriceSchedule()
					{
						MinQuantity = updated.MinQuantity,
						MaxQuantity = updated.MaxQuantity,
						UseCumulativeQuantity = updated.UseCumulativeQuantity,
						RestrictedQuantity = updated.RestrictedQuantity,
						ApplyShipping = updated.ApplyShipping,
						ApplyTax = updated.ApplyTax
					};
					var relatedPriceSchedules = await _oc.PriceSchedules.ListAllAsync(filters: $"ID={initial.ID}*");
					var priceSchedulesToUpdate = relatedPriceSchedules.Where(p => p.ID != updated.ID);
					await Throttler.RunAsync(priceSchedulesToUpdate, 100, 5, p =>
					{
						return _oc.PriceSchedules.PatchAsync(p.ID, patch, ocAuth.AccessToken);
					});
				}
				resp = await _oc.PriceSchedules.SaveAsync<PriceSchedule>(updated.ID, updated, token);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Private re-usable HasVariantChange method
		/// </summary>
		/// <param name="variant"></param>
		/// <param name="currVariant"></param>
		/// <returns>The boolean status for the HasVariantChange process</returns>
		private bool HasVariantChange(Variant variant, Variant currVariant)
		{
			var resp = false;
			try
			{
				if (variant.Active != currVariant.Active) 
				{
					resp = true; 
				}
				if (variant.Description != currVariant.Description) 
				{
					resp = true; 
				}
				if (variant.Name != currVariant.Name) 
				{
					resp = true; 
				}
				if (variant.ShipHeight != currVariant.ShipHeight) 
				{
					resp = true; 
				}
				if (variant.ShipLength != currVariant.ShipLength) 
				{
					resp = true; 
				}
				if (variant.ShipWeight != currVariant.ShipWeight) 
				{
					resp = true; 
				}
				if (variant.ShipWidth != currVariant.ShipWidth) 
				{
					resp = true; 
				}
				if (variant?.Inventory?.LastUpdated != currVariant?.Inventory?.LastUpdated) 
				{
					resp = true; 
				}
				if (variant?.Inventory?.QuantityAvailable != currVariant?.Inventory?.QuantityAvailable) 
				{
					resp = true; 
				}
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Private re-usable HandleSpecOptionChanges method
		/// </summary>
		/// <param name="requestSpecs"></param>
		/// <param name="existingSpecs"></param>
		/// <param name="token"></param>
		private async void HandleSpecOptionChanges(IList<Spec> requestSpecs, IList<Spec> existingSpecs, string token)
		{
			try
			{
				var requestSpecOptions = new Dictionary<string, List<SpecOption>>();
				var existingSpecOptions = new List<SpecOption>();
				foreach (var requestSpec in requestSpecs)
				{
					var specOpts = (from requestSpecOption in requestSpec.Options
						select requestSpecOption).ToList();
					requestSpecOptions.Add(requestSpec.ID, specOpts);
				}

				existingSpecOptions.AddRange(from existingSpec in existingSpecs
					from SpecOption existingSpecOption in existingSpec.Options
					select existingSpecOption);
				foreach (var spec in requestSpecOptions)
				{
					var changedSpecOptions = ChangedSpecOptions(spec.Value, existingSpecOptions);
					await Throttler.RunAsync(changedSpecOptions, 100, 5, option => _oc.Specs.SaveOptionAsync(spec.Key, option.ID, option, token));
				}
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
		}

		/// <summary>
		/// Private re-usable ChangedSpecOptions method
		/// </summary>
		/// <param name="requestOptions"></param>
		/// <param name="existingOptions"></param>
		/// <returns>The list of the SpecOption response objects from the ChangedSpecOptions process</returns>
		private IList<SpecOption> ChangedSpecOptions(List<SpecOption> requestOptions, List<SpecOption> existingOptions)
		{
			return requestOptions.FindAll(requestOption => OptionHasChanges(requestOption, existingOptions));
		}

		/// <summary>
		/// Private re-usable OptionHasChanges method
		/// </summary>
		/// <param name="requestOption"></param>
		/// <param name="currentOptions"></param>
		/// <returns>The boolean status for the OptionHasChanges process</returns>
		private bool OptionHasChanges(SpecOption requestOption, List<SpecOption> currentOptions)
		{
			var resp = false;
			try
			{
				var matchingOption = currentOptions.Find(currentOption => currentOption.ID == requestOption.ID);
				if (matchingOption == null)
				{
					resp = false;
				}
				if (matchingOption.PriceMarkup != requestOption.PriceMarkup)
				{
					resp = true;
				}
				if (matchingOption.IsOpenText != requestOption.IsOpenText)
				{
					resp = true;
				}
				if (matchingOption.ListOrder != requestOption.ListOrder)
				{
					resp = true;
				}
				if (matchingOption.PriceMarkupType != requestOption.PriceMarkupType)
				{
					resp = true;
				}
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return resp;
		}

		/// <summary>
		/// Private re-usable Delete task method to delete images, attachments, and assignments associated with the requested product
		/// </summary>
		/// <param name="id"></param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task Delete(string id, string token)
		{
			try
			{
				var product = await _oc.Products.GetAsync<HsProduct>(id);
				var _specs = await _oc.Products.ListSpecsAsync<Spec>(id, accessToken: token);
				var tasks = new List<Task>()
				{
					Throttler.RunAsync(_specs.Items, 100, 5, s => _oc.Specs.DeleteAsync(s.ID, accessToken: token)),
					_oc.Products.DeleteAsync(id, token)
				};
				if (product?.xp?.Images?.Count() > 0)
				{
					tasks.Add(Throttler.RunAsync(product.xp.Images, 100, 5, i => _assetClient.DeleteAssetByUrl(i.Url)));
				}
				if (product?.xp?.Documents != null && product?.xp?.Documents.Count() > 0)
				{
					tasks.Add(Throttler.RunAsync(product.xp.Documents, 100, 5, d => _assetClient.DeleteAssetByUrl(d.Url)));
				}
				// Delete images, attachments, and assignments associated with the requested product
				await Task.WhenAll(tasks);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
		}

		/// <summary>
		/// Public re-usable FilterOptionOverride task method
		/// </summary>
		/// <param name="id"></param>
		/// <param name="supplierId"></param>
		/// <param name="facets"></param>
		/// <param name="decodedToken"></param>
		/// <returns>The Product response object from the FilterOptionOverride process</returns>
		public async Task<Product> FilterOptionOverride(string id, string supplierId, IDictionary<string, object> facets, DecodedToken decodedToken)
		{
			var updatedProduct = new Product();
			try
			{
				var supplierClient = await _apiClientHelper.GetSupplierApiClient(supplierId, decodedToken.AccessToken);
				if (supplierClient == null) 
				{
					var ex = new Exception($@"The default supplier client with the SupplierID: {supplierId} was not found.");
					LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
					throw ex; 
				}

				var configToUse = new OrderCloudClientConfig
				{
					ApiUrl = decodedToken.ApiUrl,
					AuthUrl = decodedToken.AuthUrl,
					ClientId = supplierClient.ID,
					ClientSecret = supplierClient.ClientSecret,
					GrantType = GrantType.ClientCredentials,
					Roles = new[] 
					{
						ApiRole.SupplierAdmin,
						ApiRole.ProductAdmin
					},
				};

				var ocClient = new OrderCloudClient(configToUse);
				await ocClient.AuthenticateAsync();
				var token = ocClient.TokenResponse.AccessToken;
				//Format the facet data to change for request body
				var facetDataFormatted = new ExpandoObject();
				var facetDataFormattedCollection = (ICollection<KeyValuePair<string, object>>)facetDataFormatted;
				foreach (var kvp in facets)
				{
					facetDataFormattedCollection.Add(kvp);
				}
				dynamic facetDataFormattedDynamic = facetDataFormatted;

				//Update the product with a supplier token
				updatedProduct = await ocClient.Products.PatchAsync(id, new PartialProduct() { xp = new { Facets = facetDataFormattedDynamic } }, accessToken: token);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return updatedProduct;
		}

		/// <summary>
		/// Public re-usable GetSupplierNameForXpFacet task method for the GetSupplierNameForXpFacet process
		/// </summary>
		/// <param name="supplierId"></param>
		/// <param name="accessToken"></param>
		/// <returns>The SupplierName string value for the GetSupplierNameForXpFacet process</returns>
		private async Task<string> GetSupplierNameForXpFacet(string supplierId, string accessToken)
		{
			var supplier = new Supplier();
			try
			{
				supplier = await _oc.Suppliers.GetAsync(supplierId, accessToken);
			}
			catch (Exception ex)
			{
				LogExt.LogException(_settings.LogSettings, Helpers.GetMethodName(), $@"{LoggingNotifications.GetGeneralLogMessagePrefixKey()}", ex.Message, ex.StackTrace, this, true);
			}
			return supplier.Name;
		}
	}
}
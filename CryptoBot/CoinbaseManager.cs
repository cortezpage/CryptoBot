using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoinbasePro;
using CoinbasePro.Network.Authentication;
using CoinbasePro.Services.Accounts.Models;
using CoinbasePro.Services.Orders.Models.Responses;
using CoinbasePro.Services.Orders.Types;
using CoinbasePro.Services.Products.Models;
using CoinbasePro.Services.Products.Types;
using CoinbasePro.Shared.Types;
using CoinbasePro.WebSocket;

namespace CryptoBot
{
	class CoinbaseManager
	{
		// these constants may not work for all currencies and may need to be adjusted
		public const decimal MIN_BASE_SIZE = 0.01M;
		private const int BASE_NUM_DEC_PLACES = 2;
		private const int QUOTE_NUM_DEC_PLACES = 2;

		public WebSocket WebSocket { get; private set; }
		private CoinbaseProClient coinbaseClient;
		public ProductType ProdType { get; private set; }
		public Currency BaseCurrency { get; private set; }
		public Currency QuoteCurrency { get; private set; }

		public CoinbaseManager(CoinbaseCodes codes, ProductType prodType, Currency baseCurrency, Currency quoteCurrency)
		{
			ProdType = prodType;
			BaseCurrency = baseCurrency;
			QuoteCurrency = quoteCurrency;

			Log.WriteLine($"Trade pair is {prodType}. Connecting to Coinbase...");
			var authenticator = new Authenticator(codes.ApiKey, codes.ApiSecret, codes.PassPhrase);
			coinbaseClient = new CoinbaseProClient(authenticator);
		}

		public async Task<List<Candle>> GetHistoryAsync(TimeSpan timeSpan, CandleGranularity gran)
		{
			DateTime cuTime = DateTime.UtcNow;
			List<Candle> candles = (await coinbaseClient.ProductsService.GetHistoricRatesAsync(
				ProdType, cuTime - timeSpan, cuTime, gran)).ToList();
			if (candles.Count == 0) F.ErrorExit("Coinbase did not return any candles");
			if (!candles.All(x => x.Close.HasValue)) F.ErrorExit("Incomplete candle info");
			candles.Reverse();
			2.Times(() => {
				if (candles.Last().Time.Minute >= (cuTime - new TimeSpan(0, 1, 0)).Minute)
					candles.RemoveAt(candles.Count - 1);
			});
			var prodTicker = await coinbaseClient.ProductsService.GetProductTickerAsync(ProdType);
			candles.Add(new Candle() { Time = DateTime.UtcNow, Close = prodTicker.Price });
			return candles;
		}

		public async Task<decimal> GetPrice()
		{
			return (await coinbaseClient.ProductsService.GetProductTickerAsync(ProdType)).Price;
		}

		public async Task<Tuple<Account, Account>> GetAccountPairAsync()
		{
			var allAccounts = await coinbaseClient.AccountsService.GetAllAccountsAsync();
			return Tuple.Create(allAccounts.First(x => x.Currency == BaseCurrency), 
				                   allAccounts.First(x => x.Currency == QuoteCurrency));
		}

		public async Task CancelAllOrdersAsync()
		{
			await coinbaseClient.OrdersService.CancelAllOrdersAsync();
		}

		public async Task<OrderResponse> LimitBuyAsync(decimal quoteSize, decimal price)
		{
			return await LimitOrderAsync(quoteSize, false, price, OrderSide.Buy);
		}

		public async Task<OrderResponse> LimitSellAsync(decimal baseSize, decimal price)
		{
			return await LimitOrderAsync(baseSize, true, price, OrderSide.Sell);
		}

		private async Task<OrderResponse> LimitOrderAsync(decimal size, bool isBaseSize, decimal price, OrderSide orderSide)
		{
			decimal roundedPrice = Math.Round(price, QUOTE_NUM_DEC_PLACES);
			decimal baseSize = isBaseSize ? size : size / roundedPrice;
			decimal flooredBaseSize = F.FloorPl(baseSize, BASE_NUM_DEC_PLACES);
			if (flooredBaseSize < MIN_BASE_SIZE) return null;
			return await coinbaseClient.OrdersService.PlaceLimitOrderAsync(orderSide, ProdType,
				flooredBaseSize, roundedPrice);
		}

		public async Task<bool> DoesOrderIdHaveFills(Guid orderId)
		{
			return (await coinbaseClient.FillsService.
				GetFillsByOrderIdAsync(orderId.ToString(), 1, 1))[0].Count > 0;
		}
	}
}

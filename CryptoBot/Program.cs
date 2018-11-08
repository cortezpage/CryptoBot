using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TicTacTec.TA.Library;
using CoinbasePro.Services.Products.Types;
using CoinbasePro.Services.Products.Models;
using CoinbasePro.Shared.Types;
using CoinbasePro.Services.Accounts.Models;
using System.IO;
using Newtonsoft.Json;

namespace CryptoBot
{
	class Program
	{
		//                                                ENTER FILE PATH HERE
		private const string COINBASE_CODES_FILE_PATH = @"C:\CoinbaseCodes.json";

		private const decimal TRADE_THRESH_PERC = 0.3M;
		private const decimal TRADE_THRESH_MULT = 1 + TRADE_THRESH_PERC / 100;
		private const int EMA_LEN = 12;
		private const int MAIN_LOOP_DELAY_ITERS = 20;

		private CoinbaseManager cb;

		// usable means balance permitted by user to trade, available means not held
		private decimal quoteTradeSize;
		private decimal unusableBaseBal; 
		private decimal unusableQuoteBal;
		private DateTime initTime;
		private DateTime finalTime;
		private decimal initPrice;
		private decimal finalPrice;
		private Guid curBuyId = Guid.Empty;
		private Guid curSellId = Guid.Empty;

		private int iters = 0;

		async Task MainAsync()
		{
			var codes = JsonConvert.DeserializeObject<CoinbaseCodes>(File.ReadAllText(COINBASE_CODES_FILE_PATH));
			cb = new CoinbaseManager(codes, ProductType.EthUsd, Currency.ETH, Currency.USD);

			decimal startupPrice = await cb.GetPrice();
			var accountPair = await cb.GetAccountPairAsync();
			Account baseAccount = accountPair.Item1;
			Account quoteAccount = accountPair.Item2;
			quoteTradeSize = Log.PromptDecimal("Enter the trade size in quote currency.",
				x => x / startupPrice >= CoinbaseManager.MIN_BASE_SIZE * 1.5M && x <= quoteAccount.Balance, 
				"Amount must be available in balance and safely higher than minimum trade size.");
			unusableBaseBal = baseAccount.Balance;
			unusableQuoteBal = quoteAccount.Balance - quoteTradeSize;

			new Thread(async () => {
				Log.Prompt("Press to stop.");
				Log.WriteLine("Ending program...");
				await cb.CancelAllOrdersAsync();

				await PrintStats();
				Environment.Exit(0);
			}).Start();

			initPrice = await cb.GetPrice();
			initTime = DateTime.Now;

			while (true) {
				if (iters % 3 == 0 &&
					(await cb.DoesOrderIdHaveFills(curBuyId) || await cb.DoesOrderIdHaveFills(curSellId)))
				{
					await Update();
				}
				else if (iters % MAIN_LOOP_DELAY_ITERS == 0)
				{
					await Update();
				}
				F.ThreadSleepS(1);
				iters = (iters + 1) % MAIN_LOOP_DELAY_ITERS;
			}
		}

		private async Task PrintStats()
		{
			finalPrice = await cb.GetPrice();
			finalTime = DateTime.Now;

			TimeSpan timeLen = finalTime - initTime;
			decimal priceMult = finalPrice / initPrice;

			decimal initSize = quoteTradeSize;
			var balPair = await GetUsableBalances();
			decimal usableBaseBal = balPair.Item1;
			decimal usableQuoteBal = balPair.Item2;
			decimal finalSize = usableBaseBal * finalPrice + usableQuoteBal;
			decimal sizeMult = finalSize / initSize;

			Log.WriteLine("\n" +
				$"Stats\n" +
				$"\n" +
				$"Initial Time: {initTime}\n" +
				$"Final Time: {finalTime}\n" +
				$"Length: {timeLen:dd\\:hh\\:mm\\:ss}\n" +
				$"\n" +
				$"Initial Price: {initPrice:0.00}\n" +
				$"Final Price: {finalPrice:0.00}\n" +
				$"Change: {F.ChangeRep(priceMult)}\n" +
				$"Annual Change: {F.AnnualChangeRep(priceMult, timeLen)}\n" +
				$"\n" +
				$"Initial size: {initSize:0.00} {cb.QuoteCurrency}\n" +
				$"Final size: {finalSize:0.00} {cb.QuoteCurrency} " +
					$"({usableQuoteBal:0.00} {cb.QuoteCurrency} + " +
					$"{usableBaseBal:0.00} {cb.BaseCurrency})\n" +
				$"Change: {F.ChangeRep(sizeMult)}\n" +
				$"Annual Change: {F.AnnualChangeRep(sizeMult, timeLen)}\n");
		}

		private async Task Update()
		{
			List<Candle> candles = await cb.GetHistoryAsync(new TimeSpan(0, 100 + 2, 0), 
				CandleGranularity.Minutes1);
			decimal ema = (decimal)EMA(candles);
			decimal lowerLimit = ema / TRADE_THRESH_MULT;
			decimal upperLimit = ema * TRADE_THRESH_MULT;

			await cb.CancelAllOrdersAsync();

			var usableAvailBalPair = await GetUsableAvailableBalances();
			decimal usableAvailBaseBal = usableAvailBalPair.Item1;
			decimal usableAvailQuoteBal = usableAvailBalPair.Item2;

			var buyOrderResponse = await cb.LimitBuyAsync(usableAvailQuoteBal, lowerLimit);
			curBuyId = buyOrderResponse != null ? buyOrderResponse.Id : Guid.Empty;
			var sellOrderResponse = await cb.LimitSellAsync(usableAvailBaseBal, upperLimit);
			curSellId = sellOrderResponse != null ? sellOrderResponse.Id : Guid.Empty;

			await PrintStats();
		}

		private async Task<Tuple<decimal, decimal>> GetUsableAvailableBalances()
		{
			var accountPair = await cb.GetAccountPairAsync();
			Account baseAccount = accountPair.Item1;
			Account quoteAccount = accountPair.Item2;
			decimal usableAvailBaseBal = baseAccount.Available - unusableBaseBal;
			decimal usableAvailQuoteBal = quoteAccount.Available - unusableQuoteBal;
			return Tuple.Create(usableAvailBaseBal, usableAvailQuoteBal);
		}

		private async Task<Tuple<decimal, decimal>> GetUsableBalances()
		{
			var accountPair = await cb.GetAccountPairAsync();
			Account baseAccount = accountPair.Item1;
			Account quoteAccount = accountPair.Item2;
			decimal usableBaseBal = baseAccount.Balance - unusableBaseBal;
			decimal usableQuoteBal = quoteAccount.Balance - unusableQuoteBal;
			return Tuple.Create(usableBaseBal, usableQuoteBal);
		}

		private double EMA(IList<Candle> candles)
		{
			double[] closePrices = candles.Select(x => (double)x.Close).ToArray();
			double[] ema = new double[1];
			int lastInd = closePrices.Length - 1;
			Core.RetCode retCode = Core.Ema(lastInd, lastInd, closePrices, EMA_LEN, 
				out int begInd, out int numElems, ema);
			if (retCode != Core.RetCode.Success) F.ErrorExit(retCode.ToString());
			return ema[0];
		}

		private static void Main(string[] args)
		{
			new Program().MainAsync().GetAwaiter().GetResult();
		}
	}
}

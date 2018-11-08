using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBot
{
	// Contains helper functions
	public static class F
	{
		private const double DAYS_PER_YEAR = 365.2422;
		private static Random rand = new Random();

		public static void Times(this int count, Action action)
		{
			for (int i = 0; i < count; i++)
			{
				action();
			}
		}

		public static void ErrorExit(string msg)
		{
			Log.WriteLine("ERROR: " + msg);
			Environment.Exit(-1);
		}

		public static void ThreadSleepS(double timeInS)
		{
			Thread.Sleep((int)(timeInS * 1000));
		}

		public static decimal FloorPl(decimal num, int numDecimalPlaces)
		{
			decimal mult = (decimal)Math.Pow(10, numDecimalPlaces);
			return Math.Floor(num * mult) / mult;
		}

		public static decimal PercentChange(decimal mult)
		{
			return (mult - 1) * 100;
		}

		public static decimal AnnualMult(decimal multEachPeriod, TimeSpan periodLen)
		{
			double numPeriods = DAYS_PER_YEAR / periodLen.TotalDays;
			try { return (decimal)Math.Pow((double)multEachPeriod, numPeriods); }
			catch (OverflowException) { return -1; }
		}

		public static string ChangeRep(decimal mult)
		{
			return mult < 2 ? $"{PercentChange(mult):0.####}%" : $"{mult:0.####}x";
		}

		public static string AnnualChangeRep(decimal mult, TimeSpan periodLen)
		{
			decimal annualMult = AnnualMult(mult, periodLen);
			return annualMult != -1 ? ChangeRep(annualMult) : "out of range";
		}
	}
}

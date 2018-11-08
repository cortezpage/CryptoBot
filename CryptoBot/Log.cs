using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBot
{
	class Log
	{
		public static void Prompt(string msg)
		{
			WriteLine(msg);
			Console.ReadKey();
			Console.WriteLine();
		}

		public static decimal PromptDecimal(string msg, Func<decimal, bool> reqm = null, string reqmMsg = null)
		{
			WriteLine(msg);
			decimal n;
			while (true)
			{
				string nStr = Console.ReadLine();
				if (!Decimal.TryParse(nStr, out n)) {
					WriteLine("Invalid input. Enter again.");
					continue;
				}
				if (reqm != null && !reqm(n)) {
					WriteLine((reqmMsg ?? "Does not meet requirements.") + 
						" Enter again.");
					continue;
				}
				break;
			}
			return n;
		}

		public static void WriteLine(string str = "")
		{
			Console.Write($"{DateTime.Now} > {str}\n");
		}

		public static void WriteLine<T>(T value) where T : IComparable
		{
			WriteLine(value.ToString());
		}
	}
}

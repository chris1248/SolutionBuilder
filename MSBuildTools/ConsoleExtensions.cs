using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSBuildTools
{
	public class Writer
	{
		public static void Color(string s, ConsoleColor c)
		{
			var previous = Console.ForegroundColor;
			Console.ForegroundColor = c;
			Console.WriteLine(s);
			Console.ForegroundColor = previous;
		}
	}
}

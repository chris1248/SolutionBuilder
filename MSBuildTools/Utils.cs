using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Diagnostics;

namespace MSBuildTools
{
	public class Utils
	{
		public static String GetAssemblyName(String refInclude)
		{
			bool containsComma = refInclude.Contains(',');
			if (containsComma)
			{
				String[] splits = refInclude.Split(',');
				String assemblyName = splits.First(); // always the first part
				return assemblyName;
			}
			return refInclude;
		}

		public static String PathRelativeTo(String from, String to)
		{
			Uri uri1 = new Uri(from);
			Uri uri2 = new Uri(to);
			Uri relativeUri = uri1.MakeRelativeUri(uri2);

			return relativeUri.OriginalString;
		}
	}
}

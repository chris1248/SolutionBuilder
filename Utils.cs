using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Diagnostics;

namespace SolutionBuilder
{
	public class Utils
	{
		/// <summary>
		/// 
		/// </summary>
		/// <param name="condition">A raw string for the condition attribute on a ItemDefinitionGroup in an MSBuild project file. This is
		/// of the form:
		/// "'$(Configuration)|$(Platform)'=='ReleaseUnicode|Win32'"
		/// </param>
		/// <param name="config_platform">A simple configuration platform string. Of the form:
		/// "ReleaseUnicode|Win32"
		/// </param>
		/// <returns>True of they match exactly, false otherwise</returns>
		public static bool IsConfigMatch(String condition, String config_platform)
		{
			bool result = false;
			if (condition.StartsWith("'$(Configuration)|$(Platform)'=="))
			{
				String[] splits = condition.Split(new String[] { "==" }, StringSplitOptions.RemoveEmptyEntries);
				if (splits.Length == 2)
				{
					String second_part = splits[1].Trim('\'');
					if (String.Compare(config_platform, second_part) == 0)
					{
						result = true;
					}
				}
			}
			return result;
		}

		public static string ExpandMacro(Project proj, ProjectMetadataElement meta_data)
		{
			// for some reason The ExpandString method on Project doesn't resolve the OutDir macro
			// So we have to do it manually
			String raw_meta_string = meta_data.Value;
			if (raw_meta_string.Contains("$(OutDir)"))
			{
				String OutDir = proj.GetPropertyValue("OutDir");
				Debug.Assert(String.IsNullOrEmpty(OutDir) == false, "Not supposed to have an empty string for the output directory");
				raw_meta_string = raw_meta_string.Replace("$(OutDir)", OutDir);
			}
			else if (raw_meta_string.Contains("$(TargetDir)"))
			{
				raw_meta_string = raw_meta_string.Replace("$(TargetDir)", proj.GetPropertyValue("TargetDir"));
			}
			return proj.ExpandString(raw_meta_string);
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

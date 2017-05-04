using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;


namespace SolutionBuilder
{

	/// <summary>
	/// This creates a solution file.
	/// This tool searches for all project files in a given directory and then
	/// parses each file examining dependencies. It then generates a solution file
	/// with those dependencies spelled out. 
	/// This does 100% of the work of finding the dependencies, and there is no need to edit anything afterwards.
	/// </summary>
	class Program
	{
		static void PrintHelp()
		{
			Console.WriteLine("SolutionBuilder <directory> <xml build list> <output_solution_file> <configuration> <platform> [ReferenceFix]");
			Console.WriteLine("<directory>            - The directory to search for .vcxproj and .csproj files. This will search recursively.");
			Console.WriteLine("<output_solution_file> - The full path to save the solution file that is generated");
			Console.WriteLine("<configuration>        - The configuration needed to build against");
			Console.WriteLine("<platform>             - The platform needed to build against");
			Console.WriteLine("[xml build list]       - The full path to an XML file containing the official projects in the build for the product.");
			Console.WriteLine("[ProjectItemsName]     - The Item in the itemgroup for which to pull the official build list from");
		}

		static void Main(string[] args)
		{
			//System.Diagnostics.Debugger.Launch();
			DirectoryInfo search_dir;
			FileInfo output_solution_file;
			String Configuration;
			String Platform;
			if (args.Length == 4)
			{
				search_dir = new DirectoryInfo(args[0]);
				if (!search_dir.Exists)
				{
					PrintHelp();
					return;
				}
				output_solution_file = new FileInfo(args[1]);
				Configuration = args[2];
				Platform = args[3];

				bool build_parallel = false;
				SolutionBuilder sb = new SolutionBuilder(search_dir, Platform, Configuration, build_parallel);
				sb.Write(output_solution_file);
				sb.WriteDGML(search_dir.FullName, Path.GetFileNameWithoutExtension(output_solution_file.FullName));
			}
			else if (args.Length >= 5)
			{
				search_dir = new DirectoryInfo(args[0]);
				if (!search_dir.Exists)
				{
					PrintHelp();
					return;
				}

				output_solution_file = new FileInfo(args[1]);
				Configuration          = args[3];
				Platform               = args[4];
				FileInfo xml_build_list = new FileInfo(args[2]);
				String ProjectsItemName       = args[5];

				if (!xml_build_list.Exists)
				{
					Console.WriteLine("Error! The file {0} does NOT exist.", xml_build_list.FullName);
					PrintHelp();
					return;
				}

				// Constructing the SolutionBuilder instance finds and parses all projects in the tree, including dead projects.
				// It also finds dependencies between all projects.
				bool build_parallel = false;
				SolutionBuilder sb = new SolutionBuilder(search_dir, Platform, Configuration, xml_build_list, ProjectsItemName, build_parallel);
				sb.Write(output_solution_file);
				sb.PrintDebugData(Path.GetDirectoryName(output_solution_file.FullName));
/*				if (args.Length == 6 && (args[5] == "ReferenceFix"))
				{
					sb.WriteProjectReferences();
				}
				else
				{
					sb.PrintDebugData(Path.GetDirectoryName(output_solution_file.FullName));
					sb.Write(output_solution_file.FullName);
				}
				 */
			}
			else if (args.Length == 1)
			{
				if (File.Exists(args[0]))
				{
					SolutionParser parser = new SolutionParser(new FileInfo(args[0]));
					
				}
			}
			else
			{
				PrintHelp();
				return;
			}
		}
	}

}

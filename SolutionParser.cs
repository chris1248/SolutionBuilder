using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace SolutionBuilder
{
	internal class SolutionProject
	{
		public String FullPath;
		public String ID;
		public List<String> Dependencies = new List<String>();
		private String name;
		public override string ToString()
		{
			if (name == null)
			{
				name = Path.GetFileName(FullPath);
			}
			return name;
		}
	}
	internal class SolutionParser
	{
		public SolutionParser(FileInfo solution_file)
		{
			var map = Parse(solution_file);
			Print(map);
		}

		private void Print(List<SolutionProject> map)
		{
			foreach (SolutionProject project in map)
			{
				Console.WriteLine("Project: {0} - {1}", project.ID, project.FullPath);
				foreach (String dep in project.Dependencies)
				{
					int index = dep.IndexOf("3dswin\\src");
					Console.WriteLine("\t{0}", dep.Substring(index+11));
				}
				Console.WriteLine();
			}
		}

		private List<SolutionProject> Parse(FileInfo solutionFile)
		{
			String[] lines = File.ReadAllLines(solutionFile.FullName);

			var comma  = new char[]{','};
			var quotes = new char[]{'"'};
			var equals = new char[]{'='};

			var results = new List<SolutionProject>();
			var project_map = new Dictionary<String, String>();

			SolutionProject current = null;
			bool inDependencies = false;

			foreach (String line in lines)
			{
				if (line.Contains("Project(\"{"))
				{
					String[] splits = line.Split(comma, StringSplitOptions.RemoveEmptyEntries);
					Debug.Assert(splits.Length == 3);
					
					var project = new SolutionProject();
					project.FullPath = splits[1].Trim().Trim(quotes);
					project.ID = splits[2].Trim().Trim(quotes);
					results.Add(project);
					project_map.Add(project.ID, project.FullPath);
					current = project;
					continue;
				}

				if (line.Contains("ProjectSection(ProjectDependencies)"))
				{
					inDependencies = true;
					continue;
				}

				if (line.Contains("EndProjectSection"))
				{
					inDependencies = false;
					continue;
				}

				if (String.Compare("EndProject",line) == 0)
				{
					current = null;
					continue;
				}

				if (inDependencies)
				{
					String[] splits = line.Split(equals, StringSplitOptions.RemoveEmptyEntries);
					String dependentID = splits[0].Trim();
					current.Dependencies.Add(dependentID);
				}
			}

			foreach (SolutionProject project in results)
			{
				var lookupDependents = new List<String>();
				foreach (String dependent in project.Dependencies)
				{
					String dependentName = project_map[dependent];
					lookupDependents.Add(dependentName);
				}
				lookupDependents.Sort();
				// now replace it with the updated list
				project.Dependencies = lookupDependents;
			}

			results = results.OrderBy(item => item.FullPath).ToList();

			return results;
		}
		
	}
}

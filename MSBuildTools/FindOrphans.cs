using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;

namespace MSBuildTools
{
	/// <summary>
	/// Class to find orphaned files that are not part of any MSBuild project file
	/// </summary>
	public class FindOrphans
	{
		#region Data Members
		private DirectoryInfo _searchDirectory;
		private HashSet<String> _allFoundFiles   = new HashSet<String>(StringComparer.InvariantCultureIgnoreCase);
		private HashSet<String> _allProjectFiles = new HashSet<String>(StringComparer.InvariantCultureIgnoreCase);

		private IEnumerable<String> csprojs;
		private IEnumerable<String> allfiles;

		private List<CS_Project> projectFiles;
		#endregion
		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="msbuild_Files">An Array of filenames of msbuild files</param>
		/// <param name="search_directories">An array of directories to search in</param>
		/// <param name="extension">The file extension to look for</param>
		public FindOrphans(DirectoryInfo search_directory)
		{
			_searchDirectory = search_directory;
		}

		/// <summary>
		/// Prints all orphaned files to the terminal window
		/// </summary>
		/// <returns></returns>
		public int Find()
		{
			csprojs  = Directory.EnumerateFiles(_searchDirectory.FullName, "*.csproj", SearchOption.AllDirectories);
			allfiles = Directory.EnumerateFiles(_searchDirectory.FullName, "*.cs", SearchOption.AllDirectories);
			
			GetFiles(csprojs, allfiles);
			return FindTheMissingOnes();
		}

		/// <summary>
		/// Replaces the item list in the build file with a wildcard pattern with files to be
		/// included that are found, and an exclude pattern with files that were previously not
		/// included
		public void FixItemLists()
		{
			foreach (CS_Project proj in projectFiles)
			{
				Console.WriteLine(proj);
				foreach(ProjectItem item in proj.GetCompileItems)
				{
					Console.WriteLine("\t{0}", item.EvaluatedInclude);
				}
			}
		}

		private void GetFiles(IEnumerable<string> csprojs, IEnumerable<string> allfiles)
		{
			projectFiles = new List<CS_Project>();
			foreach (String csproj in csprojs)
			{
				try
				{
					var project = new CS_Project(csproj);
					projectFiles.Add(project);
					foreach (ProjectItem item in project.AllEvaluatedItems)
					{
						String itemPath = item.EvaluatedInclude;
						if (itemPath.EndsWith(".cs"))
						{
							String full = Path.GetFullPath(Path.Combine(project.DirectoryPath, itemPath));
							_allProjectFiles.Add(full);
						}
					}
				}
				catch (Exception)
				{
					Console.WriteLine("Unable to open file: {0}", csproj);
				}
			}

			foreach (String file in allfiles)
			{
				if (!file.EndsWith("g.cs"))
					_allFoundFiles.Add(file);
			}
		}

		private int FindTheMissingOnes()
		{
			var missing = new HashSet<String>(_allFoundFiles.Except(_allProjectFiles, StringComparer.OrdinalIgnoreCase));
			foreach (String orphan in missing)
			{
				Console.WriteLine("Orphaned file: {0}", orphan);
			}
			return missing.Count;
		}
	}
}

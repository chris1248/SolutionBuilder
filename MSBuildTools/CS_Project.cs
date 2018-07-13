using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;

namespace MSBuildTools
{
	/// <summary>
	/// Represents a .csproj file
	/// </summary>
	public class CS_Project : ProjectBase
	{
		public CS_Project(string path)
			: base(path)
		{
			Initialize();
			is_managed = true;
		}
		public CS_Project(FileInfo info, Dictionary<String, String> properties)
			: base(info.FullName, properties)
		{
			Initialize();
			is_managed = true;
		}
		public CS_Project(String filepath, Dictionary<String, String> properties)
			: base(filepath, properties)
		{
			Initialize();
			is_managed = true;
		}
		private String m_output_binary;
		/// <summary>
		/// Gets the lowercase, short file path of the binary that the project generates
		/// </summary>
		public override String OutputName { get { return m_output_binary; } }
		private String m_output_binary_path;
		/// <summary>
		/// Gets the Full file Path to the binary that the project generates.
		/// </summary>
		public override String OutputPath { get { return m_output_binary_path; } }

		private String m_pdb_filename;
		/// <summary>
		/// Gets the filename of the PDB file that is output. This does NOT included the path information.
		/// </summary>
		public override String PDBFileName { get { return m_pdb_filename; } }

		public override List<ProjectItem> GetCompileItems
		{
			get
			{
				/*
				var query = from item in this.Items
							where item.ItemType == "Compile"
							where item.EvaluatedInclude.ToLower().EndsWith("*.cs")
							select item;
				return query.ToList();
				*/
				
				var result = new List<ProjectItem>();
				foreach(ProjectItem item in this.Items)
				{
					if (item.ItemType == "Compile")
					{
						var include = item.EvaluatedInclude.ToLower();
						if (include.EndsWith(".cs"))
						{
							result.Add(item);
						}
					}
				}
				return result;
			}
		}

		private void Initialize()
		{
			String TargetName = GetPropertyValue("AssemblyName");
			// File extensions for all csproj files are always .dll
			String TargetExt = GetPropertyValue("TargetExt");
			m_output_binary = (TargetName + TargetExt).ToLower();

			m_output_binary_path = Path.Combine(GetPropertyValue("OutputPath"), m_output_binary);
			// CSharp projects don't allow you to specify the .pdb path. They are 
			// determined automatically
			m_pdb_filename = (TargetName + ".pdb").ToLower();

			// All C# projects have reference assemblies, however not all C++ projects have them.
			GatherReferenceAssemblies();
		}

		public override string ToString()
		{
			return "{" + Path.GetFileName(FullPath) + "}";
		}

		public override void ConvertItemRefs()
		{
			// first identify any items to be removed
			var toBeRemoved = new List<ProjectItem>();
			var parents = new HashSet<ProjectElementContainer>();
			foreach (ProjectItem item in this.GetItems("Compile"))
			{
				// If the item is outside of the project directory, then don't remove it.
				// Perhaps say a shared file that a lot of project files include
				if (!Utils.IsPathInDirectory(this.DirectoryPath, Path.Combine(this.DirectoryPath, item.EvaluatedInclude)))
					continue;
				
				// Need to remove it
				toBeRemoved.Add(item);
			}

			// keep track of childless parents.
			var childless = new List<ProjectElementContainer>();
			// second remove them
			foreach (var item in toBeRemoved)
			{
				var p = item.Xml.Parent;
				if (p != null)
				{
					p.RemoveChild(item.Xml);
					if (parents.Contains(p) == false)
						parents.Add(p);
					if (p.Count == 0)
						childless.Add(p);
				}
			}

			

			if (parents.Count == 0)
				return; // Nothing was removed

			// Re-Add all the items again
			var firstParent = parents.First();
			ProjectItemGroupElement compile_group = firstParent as ProjectItemGroupElement;
			ProjectItemElement compile = compile_group.AddItem("Compile", "**\\*.cs");

			var sb = new StringBuilder();
			foreach (var orphan in GetOrphans())
			{
				sb.Append(String.Format("{0};", Utils.PathRelativeTo(this.DirectoryPath, orphan)));
				compile.Exclude = sb.ToString();
			}

			foreach (var p in childless)
			{
				p.Parent.RemoveChild(p);
			}

			this.Save();
		}

		public override List<String> GetOrphans()
		{
			IEnumerable<String> allfiles = Directory.EnumerateFiles(this.DirectoryPath, "*.cs", SearchOption.AllDirectories);

			HashSet<String> allFoundFiles = new HashSet<String>(StringComparer.InvariantCultureIgnoreCase);
			HashSet<String> allProjectFiles = new HashSet<String>(StringComparer.InvariantCultureIgnoreCase);

			foreach (ProjectItem item in this.AllEvaluatedItems)
			{
				String itemPath = item.EvaluatedInclude;
				if (itemPath.EndsWith(".cs"))
				{
					String full = Path.GetFullPath(Path.Combine(this.DirectoryPath, itemPath));
					allProjectFiles.Add(full);
				}
			}

			foreach (String file in allfiles)
			{
				if (!file.EndsWith("g.cs"))
					allFoundFiles.Add(file);
			}

			var missing = new HashSet<String>(allFoundFiles.Except(allProjectFiles, StringComparer.OrdinalIgnoreCase));
			return missing.ToList();
		}
	}
}

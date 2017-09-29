using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace MSBuildTools
{
	/// <summary>
	/// Generic class that represents an MSBuild project file. It provides common methods for extracting 
	/// dependencies, and output file names.
	/// </summary>
	public abstract class ProjectBase : Microsoft.Build.Evaluation.Project
	{
		public ProjectBase(String file_path)
			: base(file_path)
		{

		}

		public ProjectBase(String file_path, Dictionary<String, String> global_properties)
			: base(file_path, global_properties, null)
		{
			
		}
		/// <summary>
		/// The output filename that is built by this project.
		/// </summary>
		abstract public String OutputName { get; }
		/// <summary>
		/// The full path to the output file that is built by this project.
		/// </summary>
		abstract public String OutputPath { get; }
		/// <summary>
		/// The PDB filename for this project
		/// </summary>
		abstract public String PDBFileName { get; }
		/// <summary>
		/// Public bool to help in sorting
		/// </summary>
		public bool Visited = false;
		
		public override string ToString()
		{
			return String.Format("ProjectBase: {0}", Path.GetFileName(this.FullPath));
		}

		abstract public List<ProjectItem> GetCompileItems { get; }

		// Both C++ and C# projects can be compiled as managed, hence BOTH use the same XML markup 
		// to represent reference assemblies
		protected int GatherReferenceAssemblies()
		{
			// Find the Referenced Assemblies
			var project_references = this.GetItems("Reference");

			int numReferences = 0;
			foreach (ProjectItem Ref in project_references)
			{
				// If it has a HintPath, Use it.
				String hintPath = null;
				foreach (var meta in Ref.DirectMetadata)
				{
					if (meta.Name == "HintPath")
					{
						String evaluated_hint = meta.EvaluatedValue;
						if (String.IsNullOrEmpty(Path.GetExtension(evaluated_hint)) == false)
						{
							hintPath = evaluated_hint;
							break;
						}
					}
				}

				if (hintPath != null)
				{
					String assembly_name = Path.GetFileName(hintPath).Trim().ToLower();
					reference_assemblies.Add(assembly_name);
					numReferences++;
					continue;
				}

				
				// The hint path does not always specify the assembly name, sometimes just specifies the directory
				// So use the Include attribute from the XML
				String eval_include = Ref.EvaluatedInclude;
				// ignore all the version information
				String[] splits = eval_include.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				if (splits.Length > 0)
				{
					// The assembly name is always the first one
					String assembly_name = splits[0].Trim().ToLower();
					// Note the assembly name doesn't have the typical .dll suffix on it.
					reference_assemblies.Add(assembly_name);
					numReferences++;
				}
			}
			return numReferences;
		}

		public void ConvertReference(string elemnentType, Dictionary<string, ProjectBase> assemblyNames)
		{
			// First remove any existing references to these projects since they will be
			// replaced by ProjectReferences
			var toBeRemoved = new List<ProjectItem>();
			var parents = new HashSet<ProjectElementContainer>();
			foreach (ProjectItem normalRef in this.GetItems(elemnentType))
			{
				var assemblyInclude = Utils.GetAssemblyName(normalRef.EvaluatedInclude);
				if (assemblyNames.ContainsKey(assemblyInclude))
				{
					// Need to remove it
					toBeRemoved.Add(normalRef);
				}
			}
			foreach (var item in toBeRemoved)
			{
				var p = item.Xml.Parent;
				p.RemoveChild(item.Xml);
				if (parents.Contains(p) == false)
					parents.Add(p);
			}

			// Re-Add all Project References again
			if (this.GetDependencies().Count() > 0)
			{
				var firstParent = parents.First();
				ProjectItemGroupElement ref_group = firstParent as ProjectItemGroupElement;
				foreach (ProjectBase dependency in this.GetDependencies())
				{
					ProjectItemElement item =
						ref_group.AddItem("ProjectReference", Utils.PathRelativeTo(this.FullPath, dependency.FullPath));
					item.AddMetadata("Project", dependency.GetPropertyValue("ProjectGuid"));
					item.AddMetadata("Name", dependency.GetPropertyValue("AssemblyName"));
				}
			}
		}

		abstract public List<String> GetOrphans();

		abstract public void ConvertItemRefs();

		protected bool is_managed = false;
		public bool IsManaged
		{
			get { return is_managed; }
		}

		private List<String> reference_assemblies = new List<String>();

		/// <summary>
		/// Gets All referenced assemblies as specified by the XML. This includes both internal and external assemblies.
		/// This is used as a precursor or first step to the more powerful functions below of 
		/// AddDependency and GetDependencies which return real ProjectBase instances and not mere strings.
		/// </summary>
		/// <returns>A simple array of string names of the assembly file names, each ususally has no file extension</returns>
		public String[] GetReferenceAssemblies()
		{
			return reference_assemblies.ToArray();
		}
		public void AddReferenceAssembly(String assembly_name)
		{
			if (reference_assemblies.Contains(assembly_name) == false)
			{
				reference_assemblies.Add(assembly_name);
			}
		}

		/// <summary>
		/// A list of ProjectBase instances that this project depends on.
		/// </summary>
		private List<ProjectBase> dependencies = new List<ProjectBase>();
		/// <summary>
		/// A list of the other projects that this project directly depends on.
		/// </summary>
		public ProjectBase[] GetDependencies()
		{
			return dependencies.ToArray();
		}
		/// <summary>
		/// Adds a dependency to this project for another project that is ALSO actually in the build, and not a pre-built,
		/// third party dependency like a Microsoft DLL.
		/// </summary>
		/// <param name="proj">The project that this instance depends on</param>
		public void AddDependency(ProjectBase proj)
		{
			if (!dependencies.Contains(proj))
			{
				dependencies.Add(proj);
			}
		}

		private List<ProjectBase> mAllDependencies = new List<ProjectBase>();

		public ProjectBase[] GetRecursiveDependents()
		{
			RecurseDependencies(this);
			return mAllDependencies.ToArray();
		}

		/// <summary>
		/// Recursively gets all dependents including this project itself.
		/// </summary>
		/// <param name="project"></param>
		private void RecurseDependencies(ProjectBase project)
		{
			if (mAllDependencies.Contains(project))
			{
				return;
			}
			else
			{
				mAllDependencies.Add(project);
			}

			foreach (ProjectBase dependent in dependencies)
			{
				RecurseDependencies(dependent);
			}
		}
	}
}

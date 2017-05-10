using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Threading.Tasks;
using System.Xml;
using MSBuildTools;
using System.Text.RegularExpressions;

namespace MSBuildTools
{
	/// <summary>
	/// Represents a duplicate where more than one project file writes to the same binary file
	/// </summary>
	class Duplicate
	{
		/// <summary>
		/// The name of the binary that is output by the .vcxproj or .csproj project.
		/// This is the name of the file only without the directory information.
		/// </summary>
		public String File;
		/// <summary>
		/// List of the projects that contain duplicate output names.
		/// </summary>
		public List<ProjectBase> Projects = new List<ProjectBase>();
	}

	/// <summary>
	/// Class reponsable for parsing projects and generating a visual studio solution file (*.sln)
	/// </summary>
	public class SolutionBuilder
	{
		/// <summary>
		/// Constructor
		/// </summary>
		private SolutionBuilder()
		{
		}
		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="dir">The directory to recursively search for .vcxproj and .csproj files.</param>
		/// <param name="Platform">Either Win32 or x64.</param>
		/// <param name="Config">The Build configuration for each project. For instance Debug / Release.</param>
		/// <param name="xml_build_list">Full path to an XML file that contains a list of files that are officially in the build, as specified by the build system.</param>
		/// <param name="parallel">True to iterate project files in Parallel using multiple threads. False to use a single thread.</param>
		public SolutionBuilder(DirectoryInfo dir, String Platform, String Config, FileInfo xml_build_list, String itemsName, bool parallel)
		{
			search_dir = dir;
			platform = Platform;
			configuration = Config;
			config_platform = String.Format("{0}|{1}", configuration, platform);

			InitializeBuildList(xml_build_list, itemsName);
			
			if (parallel)
			{
				InitializeParallel();
			}
			else
			{
				Initialize();
			}
		}

		public SolutionBuilder(DirectoryInfo dir, String Platform, String Config, bool parallel)
		{
			search_dir = dir;
			platform = Platform;
			configuration = Config;
			config_platform = String.Format("{0}|{1}", configuration, platform);

			if (parallel)
			{
				InitializeParallel();
			}
			else
			{
				IntializeAll();
			}
		}

		private void InitializeBuildList(FileInfo xml_build_list, String itemsName)
		{
			Microsoft.Build.Evaluation.Project buildFileList = new Microsoft.Build.Evaluation.Project(xml_build_list.FullName, null, null);

			ICollection<ProjectItem> items = buildFileList.GetItems(itemsName);

			foreach (var item in items)
			{
				String evaledPath = Path.GetFullPath(item.EvaluatedInclude);
				if (String.IsNullOrEmpty(evaledPath))
					continue;

				String path = evaledPath.ToLower();
				if (!File.Exists(path))
				{
					Console.WriteLine("Error! Project specified in the build file doesn't exist: {0}", item.UnevaluatedInclude);
					continue;
				}
				if (!mBuildList.Contains(path))
				{
					mBuildList.Add(path);
				}
			}

			extra_dependencies = buildFileList.GetItems("ExtraDependencies");

		}
		private void InitializeParallel()
		{
			GatherProjectsParallel();
			GatherDependencies();
			GenerateFinalBuildList();
		}

		private void Initialize()
		{
			GatherProjects();
			GatherDependencies();
			GenerateFinalBuildList();
		}

		private void IntializeAll()
		{
			GatherProjects();
			GatherDependencies();
			GenerateBuildListAll();
		}

		// ================================================================================
		// ================================================================================
		#region Data Lists

		private DirectoryInfo search_dir;
		private String platform;
		private String configuration;
		private String config_platform;

		/// <summary>
		/// A List of ALL vcxproj files found in the search directory. All are here if they are in the build or not. 
		/// </summary>
		private List<VC_Project> all_vc_projects = new List<VC_Project>();
		/// <summary>
		/// A List of ALL csproj files found in the search directory. All are here if they are in the build or not. 
		/// </summary>
		private List<CS_Project> all_cs_projects = new List<CS_Project>();
		/// <summary>
		/// A List of all projects found in the search directory whether or not they are in the build.
		/// This contains a mapping of output binary names to project files. 
		/// The Key is the short file name of the DLL or output binary (case insensitive)
		/// The Value is the MSBuild Project instance
		/// This is NOT the final list of what will get built or put in the solution file. This file will actually
		/// contain more projects that get put in the product since this container will contain listings for all
		/// projects found in the source tree.
		/// Note, because this is a dictionary, there cannot be two projects in here that both write to 
		/// to the same output file.
		/// </summary>
		private Dictionary<String, ProjectBase> all_projectnames_map = new Dictionary<String, ProjectBase>(StringComparer.OrdinalIgnoreCase);
		/// <summary>
		/// Contains map of projects that output to the same output binary file. So if two project files
		/// write to the same file name, this will contain the path of the second project file found. 
		/// This is FORBIDDEN in the build.
		/// Key = The output binary file name.
		/// Value = An instance of Duplicate.
		/// </summary>
		private Dictionary<String, Duplicate> all_duplicate_projects = new Dictionary<String, Duplicate>(StringComparer.OrdinalIgnoreCase);
		/// <summary>
		/// Helps to determine how many times a project is reference by other projects.
		/// Key = The project
		/// Value = the count of how  many times it is used
		/// </summary>
		private Dictionary<ProjectBase, UInt16> UsedByCount = new Dictionary<ProjectBase, UInt16>();
		// -----------------------------------------------
		// -----------------------------------------------

		/// <summary>
		/// A list of project files that are specified in any official, cannonical file list
		/// This is simply a list of those project file names.
		/// This is NOT the final list of what will get built or put in the solution file. 
		/// There could be many more files not specified that need to be built that are not specified in this file.
		/// </summary>
		private List<String> mBuildList = new List<String>();

		/// <summary>
		/// A List of project file names that are NOT in the build.
		/// </summary>
		private List<ProjectBase> ignored_projects = new List<ProjectBase>();

		/// <summary>
		/// The final list of projects that will get built for the product. 
		/// This list will contain more projects than that specified in the build product XML file since
		/// the XML file does not contain library projects (.lib) nor does it contain all dependencies
		/// </summary>
		private List<ProjectBase> build_products = new List<ProjectBase>();

		/// <summary>
		/// Holds a extra dependencies that solution builder is unable to decipher on it's own. This 
		/// consists of an itemgroup in an msbuild file called specifically 'ExtraDependencies'. It consists
		/// of a list of projects, and a meta-data element that holds the name of the a project they depend on.
		/// </summary>
		private ICollection<ProjectItem> extra_dependencies = new List<ProjectItem>();

		/// <summary>
		/// Used to check for duplicate GUID's. It's case insensitive
		/// </summary>
		private HashSet<String> all_guids = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
		#endregion
		// ================================================================================
		// ================================================================================

		private void GatherProjectsParallel()
		{
			Stopwatch timer = Stopwatch.StartNew();

			IEnumerable<FileInfo> cs_projs = from file in search_dir.EnumerateFiles("*.csproj", SearchOption.AllDirectories).AsParallel()
											 select file;

			IEnumerable<FileInfo> vc_projs = from file in search_dir.EnumerateFiles("*.vcxproj", SearchOption.AllDirectories).AsParallel()
											 select file;

			List<FileInfo> all_projs = new List<FileInfo>();
			all_projs.AddRange(cs_projs);
			all_projs.AddRange(vc_projs);
			// In order to open the project files correctly, we have to set the build platform and configuration
			Dictionary<String, String> global_properties = new Dictionary<String, String>();
			global_properties.Add("Platform", platform);
			global_properties.Add("Configuration", configuration);

			// Because this works in parallel we must store all results in a thread_safe container for
			// parallel execution
			var thrd_all_projects_map  = new ConcurrentDictionary<String, ProjectBase>(StringComparer.OrdinalIgnoreCase);
			var thrd_all_cs_projects   = new ConcurrentBag<CS_Project>();
			var thrd_all_vc_projects   = new ConcurrentBag<VC_Project>();
			var thrd_duplicate_outputs = new ConcurrentDictionary<String, Duplicate>(StringComparer.OrdinalIgnoreCase);
			var thrd_duplicate_guids   = new ConcurrentBag<String>();

			Parallel.ForEach(all_projs, (file) =>
			{
				ProjectBase project = null;
				if (String.Compare(".csproj", file.Extension, true) == 0)
				{
					project = new CS_Project(file, global_properties);
					// 1. Check for duplicate GUIDS
					String guid = project.GetPropertyValue("ProjectGuid");

					if (thrd_duplicate_guids.Contains(guid))
					{
						Writer.Color(String.Format("Error! Duplicate GUID {0} found in file: {1}", guid, project.FullPath), ConsoleColor.Red);
						ignored_projects.Add(project);
						return;
					}
					else
						thrd_duplicate_guids.Add(guid);

					// 2. Check the output binary path is not a duplicate either
					if (thrd_duplicate_outputs.ContainsKey(project.OutputPath))
					{
						Writer.Color(String.Format("Error! Duplicate output path {0} found in file: {1}", project.OutputPath, project.FullPath), ConsoleColor.Red);
						ignored_projects.Add(project);
						return;
					}
					else
					{
						var duplicate = new Duplicate { File = project.OutputPath };
						duplicate.Projects.Add(project);
						thrd_duplicate_outputs.TryAdd(project.OutputPath, duplicate);
					}

					thrd_all_cs_projects.Add(project as CS_Project);
				}
				else if (String.Compare(".vcxproj", file.Extension, true) == 0)
				{
					project = new VC_Project(file, global_properties);
					// 1. Check for duplicate GUIDS
					String guid = project.GetPropertyValue("ProjectGuid");

					if (thrd_duplicate_guids.Contains(guid))
					{
						Writer.Color(String.Format("Error! Duplicate GUID {0} found in file: {1}", guid, project.FullPath), ConsoleColor.Red);
						ignored_projects.Add(project);
						return;
					}
					else
						thrd_duplicate_guids.Add(guid);

					// 2. Check the output binary path is not a duplicate either
					if (thrd_duplicate_outputs.ContainsKey(project.OutputPath))
					{
						Writer.Color(String.Format("Error! Duplicate output path {0} found in file: {1}", project.OutputPath, project.FullPath), ConsoleColor.Red);
						ignored_projects.Add(project);
						return;
					}
					else
					{
						var duplicate = new Duplicate { File = project.OutputPath };
						duplicate.Projects.Add(project);
						thrd_duplicate_outputs.TryAdd(project.OutputPath, duplicate);
					}
					thrd_all_vc_projects.Add(project as VC_Project);
				}
			});


			timer.Stop();
			Console.WriteLine("Elapsed Time (Gather Projects)          : {0}", timer.Elapsed);

			// Now that the big threading part is done, save out data from the thread-safe containers
			// back to the usual containers.
			all_projectnames_map   = thrd_all_projects_map.ToDictionary(p => p.Key, p => p.Value);
			all_duplicate_projects = thrd_duplicate_outputs.ToDictionary(p => p.Key, p => p.Value);
			all_vc_projects = thrd_all_vc_projects.ToList();
			all_cs_projects = thrd_all_cs_projects.ToList();
		}

		/// <summary>
		/// Single threaded iteration of all projects files in the specified directory.
		/// This can be slow.
		/// </summary>
		private void GatherProjects()
		{
			Stopwatch timer = Stopwatch.StartNew();
			
			IEnumerable<FileInfo> cs_projs = from file in search_dir.EnumerateFiles("*.csproj", SearchOption.AllDirectories).AsParallel()
											 select file;

			IEnumerable<FileInfo> vc_projs = from file in search_dir.EnumerateFiles("*.vcxproj", SearchOption.AllDirectories).AsParallel()
											 select file;

			// In order to open the project files correctly, we have to set the build platform and configuration
			Dictionary<String, String> global_properties = new Dictionary<String, String>();
			global_properties.Add("Platform", platform);
			global_properties.Add("Configuration", configuration);
			int bad_count = 0;
			foreach (FileInfo file in cs_projs)
			{
				try
				{
					CS_Project msproject = new CS_Project(file, global_properties);
					if (CheckForDuplicates(msproject))
						continue;
					all_projectnames_map.Add(msproject.OutputName, msproject);
					all_cs_projects.Add(msproject);
				}
				catch (System.Exception e)
				{
					Writer.Color(String.Format("Error opening file: {0}", file.FullName), ConsoleColor.Red);
					Console.WriteLine(e.Message);
					bad_count++;
				}
			}
			foreach (FileInfo file in vc_projs)
			{
				try
				{
					VC_Project msproject = new VC_Project(file, global_properties);
					if (CheckForDuplicates(msproject))
						continue;
					all_projectnames_map.Add(msproject.OutputName, msproject);
					all_vc_projects.Add(msproject);
				}
				catch (System.Exception e)
				{
					Writer.Color(String.Format("Error opening file: {0}", file.FullName), ConsoleColor.Red);
					Console.WriteLine(e.Message);
					bad_count++;
				}
			}

			Console.WriteLine("Found {0} total files", all_projectnames_map.Count + bad_count);
			Console.WriteLine("Found {0} valid files", all_projectnames_map.Count);
			Console.WriteLine("Found {0} bad files", bad_count);
			Console.WriteLine("Found {0} vc project files", all_vc_projects.Count);
			Console.WriteLine("Found {0} cs project files", all_cs_projects.Count);
			Console.WriteLine("Files in Build List: {0}", mBuildList.Count);
			timer.Stop();
			Writer.Color(String.Format("Elapsed Time(Gather Projects): {0}", timer.Elapsed), ConsoleColor.Cyan);
		}

		/// <summary>
		/// Checks for duplicate GUID's and duplicate output path's.
		/// </summary>
		/// <param name="project">The project to check</param>
		/// <returns>true if not a duplicate</returns>
		private bool CheckForDuplicates(ProjectBase project)
		{
			// 1. Check for duplicate GUIDS
			String guid = project.GetPropertyValue("ProjectGuid");
			if (all_guids.Contains(guid))
			{
				var newGuid = "{" + Guid.NewGuid() + "}";
				project.SetProperty("ProjectGuid", newGuid);
				project.Save();
				Writer.Color(String.Format("Warning! fixing duplicate GUID {0} found in file: {1}", guid, project.FullPath), ConsoleColor.Yellow);
				return true;
			}
			else
				all_guids.Add(guid);

			// 2. Check the output binary path is not a duplicate either
			if (all_duplicate_projects.ContainsKey(project.OutputPath))
			{
				Writer.Color(String.Format("Error! Duplicate output path {0} found in file: {1}", project.OutputPath, project.FullPath), ConsoleColor.Red);
				return false;
			}
			else
			{
				var duplicate = new Duplicate { File = project.OutputPath };
				duplicate.Projects.Add(project);
				all_duplicate_projects.Add(project.OutputPath, duplicate);
			}
			return false;
		}

		/// <summary>
		/// Hooks up dependencies between projects. This is super important.
		/// </summary>
		private void GatherDependencies()
		{
			Stopwatch timer = Stopwatch.StartNew();

			int countDependencies = 0;
			// 0. Prep work
			// Useful for associating the project file name with an instance of ProjectBase
			// Key is the filenam of the project, it is used in a case insensitive way
			var map_all_projects = new Dictionary<string, ProjectBase>(StringComparer.OrdinalIgnoreCase);
			foreach (ProjectBase proj in all_projectnames_map.Values)
			{
				String key = Path.GetFileName(proj.FullPath);
				if (!map_all_projects.ContainsKey(key))
					map_all_projects.Add(key, proj);
			}

			// 1. Gather projects with MIDL outputs.
			// Some old dinosour projects have Midl properties and specify type library output files.
			// The thing is that most projects don't use these, and this is just a holdout of some old old project settings that
			// should be thrown away.
			var map_type_libraries = new Dictionary<string, VC_Project>(StringComparer.OrdinalIgnoreCase);
			foreach(VC_Project project in all_vc_projects) {
				if (project.Typelibrary != null) {
					// a project can specify #import *.dll or #import *.tbl. Therefore remove the file extension
					// and only store the name
					String tlName = Path.GetFileNameWithoutExtension(project.Typelibrary);
					if (!map_type_libraries.ContainsKey(tlName)) {
						map_type_libraries.Add(tlName, project);
					}
				}
			}

			// 2. Gather projects with .lib outputs
			// Contains listings of all the library files generated by each native c/c++ project
			// the Key is the lowercase short name of the library file generated by the project
			// the Value is the VC_Project
			var map_import_libraries = new Dictionary<String, VC_Project>(StringComparer.OrdinalIgnoreCase);
			foreach (VC_Project project in all_vc_projects) {
				if (!String.IsNullOrEmpty(project.ImportLibrary)) {
					if (!map_import_libraries.ContainsKey(project.ImportLibrary))
						map_import_libraries.Add(project.ImportLibrary, project);
				}
			}

			// 3. Hook up dependencies for c/c++ projects using their library (*.lib) and MIDL dependencies.
			foreach (VC_Project proj in all_vc_projects)
			{
				// Most of our projects have these types of dependencies
				foreach (String input_lib in proj.InputLibraries)
				{
					if (map_import_libraries.ContainsKey(input_lib))
					{
						VC_Project dependency = map_import_libraries[input_lib];
						proj.AddDependency(dependency);
						countDependencies++;
					}
				}
				// Very few of our projects actually have dependencies through type libraries (MIDL)
				foreach (String type_lib in proj.InputTypeLibraries)
				{
					// a project can specify #import *.dll or #import *.tbl. Therefore check for the name
					// without the extension
					String tlName = Path.GetFileNameWithoutExtension(type_lib);
					if (map_type_libraries.ContainsKey(tlName))
					{
						VC_Project dependency = map_type_libraries[tlName];
						proj.AddDependency(dependency);
						countDependencies++;
					}
				}
			}

			// 4. Find managed code dependencies
			// Some vcxproj files compile to managed code, but not very many. BOTH vcxproj and csproj can depend on these 
			// managed C++ projects.
			foreach (ProjectBase proj in all_projectnames_map.Values)
			{
				foreach (String assembly_name in proj.GetReferenceAssemblies())
				{
					String assembly_file = assembly_name;
					// Some assemblies already have the .dll extension. If it doesn't have it, then add it.
					if (assembly_file.EndsWith(".dll") == false)
					{
						assembly_file = assembly_file + ".dll";
					}

					if (all_projectnames_map.ContainsKey(assembly_file))
					{
						ProjectBase dependent = all_projectnames_map[assembly_file];
						proj.AddDependency(dependent);
						countDependencies++;
					}
					else
					{
						// It depends on something outside of our project. skip it
					}
				}
			}

			// 5. Find dependencies listed specified in the msbuild file passed to this class.
			foreach (var item in extra_dependencies)
			{
				String evaledPath = Path.GetFullPath(item.EvaluatedInclude);
				if (String.IsNullOrEmpty(evaledPath))
					continue;

				String path = evaledPath.ToLower();
				if (!File.Exists(path))
				{
					Console.WriteLine("Error! Project specified in the build file doesn't exist: {0}", item.UnevaluatedInclude);
					continue;
				}
				String dependsOn = item.GetMetadataValue("DependsOn");
				if (!File.Exists(dependsOn))
				{
					Console.WriteLine("Error! Project specified in the build file doesn't exist: {0}", item.UnevaluatedInclude);
					continue;
				}

				String keyfilename = Path.GetFileName(path).ToLower();
				String dependsfilename = Path.GetFileName(dependsOn).ToLower();
				if (map_all_projects.ContainsKey(keyfilename))
				{
					ProjectBase keyProject = map_all_projects[keyfilename];
					if (map_all_projects.ContainsKey(dependsfilename))
					{
						ProjectBase dependsProject = map_all_projects[dependsfilename];
						keyProject.AddDependency(dependsProject);
						countDependencies++;
					}
				}
			}

			timer.Stop();
			Console.WriteLine("found {0} dependencies", countDependencies);
			Writer.Color(String.Format("Elapsed Time (Gather Dependencies): {0}", timer.Elapsed), ConsoleColor.Cyan);
		}

		private void GenerateFinalBuildList()
		{
			Stopwatch timer = Stopwatch.StartNew();
			
			foreach (var pair in all_projectnames_map)
			{
				String project_file = pair.Value.FullPath.ToLower();
				// Start from the build list and find all dependencies from there.
				if (mBuildList.Contains(project_file))
				{
					ProjectBase proj = pair.Value;
					RecurseDependencies(proj);
				}
			}

			foreach (ProjectBase proj in all_projectnames_map.Values)
			{
				if (build_products.Contains(proj) == false)
				{
					ignored_projects.Add(proj);
				}
			}

			build_products = build_products.OrderBy(p => p.FullPath).ToList();

			timer.Stop();
			// PrintProjectMetrics(timer);
		}

		private void GenerateBuildListAll()
		{
			Stopwatch timer = Stopwatch.StartNew();
			foreach (var pair in all_projectnames_map)
			{
				mBuildList.Add(pair.Key.ToLower());
				build_products.Add(pair.Value);
			}
			timer.Stop();
			Writer.Color(String.Format("Elapsed Time (Generate Build List): {0}", timer.Elapsed), ConsoleColor.Cyan);
		}

		private void PrintProjectMetrics(Stopwatch timer)
		{
			Console.WriteLine("Elapsed Time (Generate Final Build List): {0}", timer.Elapsed);
			int numProjects = all_projectnames_map.Count + all_duplicate_projects.Count;
			Console.WriteLine("Found {0} project files", numProjects);
			Console.WriteLine("Found {0} vc project files", all_vc_projects.Count);
			Console.WriteLine("Found {0} cs project files", all_cs_projects.Count);
			Console.WriteLine("Found {0} in build list", mBuildList.Count);
			Console.WriteLine("Number of Projects in Solution: {0}", build_products.Count);
			Console.WriteLine("Number of Projects NOT in build: {0}", numProjects - build_products.Count);

			if (all_duplicate_projects.Count > 0)
			{
				ConsoleColor previous = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error! Found {0} projects with duplicate output paths.", all_duplicate_projects.Count);
				foreach (Duplicate dup in all_duplicate_projects.Values)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.Write("Error! Duplicate project output path: ");
					Console.ForegroundColor = ConsoleColor.Cyan;
					Console.Write("{0}\n", dup.File);

					Console.ForegroundColor = ConsoleColor.Yellow;
					foreach (ProjectBase path in dup.Projects)
					{
						Console.Write("\t{0}\n", path.FullPath);
					}
				}
				Console.ForegroundColor = previous;
			}
		}

		private void RecurseDependencies(ProjectBase project)
		{
			if (!build_products.Contains(project))
			{
				build_products.Add(project);
			}

			foreach (Project dependency in project.GetDependencies())
			{
				RecurseDependencies(dependency as ProjectBase);
			}
		}

		public void PrintDebugData(String directory)
		{
			// This method dumps a list of the dlls being build for later use in the build process
			// when doing digital signing of the dlls we create. For each project
			// dump the name of its output files, unless it is a .lib file.
			// Only files included in the build directory are included here.
			// That means that binaries built somewhere else are excluded.
			using (StreamWriter sw = new StreamWriter(Path.Combine(directory, "build_list.txt")))
			{
				ProjectBase pb = build_products[0];

				var query = from proj in build_products
							where proj.OutputName != null
							let extension = Path.GetExtension(proj.OutputName)
							where !extension.Contains(".lib")
							orderby proj.FullPath
							select proj;

				foreach (ProjectBase proj in query)
				{
					sw.WriteLine(proj.OutputPath);
				}
			}

			using (StreamWriter sw = new StreamWriter( Path.Combine(directory, "projects_all.txt") ))
			{
				var query = from proj in all_projectnames_map.Values
							where proj.OutputName != null
							orderby proj.FullPath
							select proj;

				foreach (ProjectBase proj in query)
				{
					sw.WriteLine("{0,50}\t{1}", proj.OutputName, proj.FullPath);
					if (proj.GetDependencies().Length > 0)
					{
						sw.WriteLine("{0,50}{1}", String.Empty, "Depends on");
						foreach (var dependency in proj.GetDependencies())
						{
							sw.WriteLine("{0,50}{1}", String.Empty, (dependency as ProjectBase).OutputName);
						}
					}
				}
			}

			using (StreamWriter sw = new StreamWriter(Path.Combine(directory, "projects_in_build.txt")))
			{
				foreach (ProjectBase proj in build_products)
				{
					sw.WriteLine("{0,50} - {1}", proj.OutputName, proj.FullPath);
				}
			}

			ignored_projects = ignored_projects.OrderBy(p => p.FullPath).ToList();
			using (StreamWriter sw = new StreamWriter(Path.Combine(directory, "projects_not_in_build.txt")))
			{
				foreach (ProjectBase proj in ignored_projects)
				{
					sw.WriteLine("{0,50} - {1}", proj.OutputName, proj.FullPath);
				}
			}

			using (StreamWriter sw = new StreamWriter(Path.Combine(directory, "projects_managed_vc.txt")))
			{
				foreach (ProjectBase proj in all_vc_projects)
				{
					if (proj.IsManaged)
					{
						sw.WriteLine("{0,50} - {1}", proj.OutputName, proj.FullPath);
					}
				}
			}

			using (StreamWriter sw = new StreamWriter(Path.Combine(directory, "projects_native_libraries.txt")))
			{
				foreach (ProjectBase proj in all_vc_projects)
				{
					if (String.Compare(".lib", Path.GetExtension(proj.OutputName), true) == 0)
					{
						sw.WriteLine("{0,50} - {1}", proj.OutputName, proj.FullPath);
					}
				}
			}

			using (StreamWriter sw = new StreamWriter(Path.Combine(directory, "projects_PDB_files.txt")))
			{
				foreach (ProjectBase proj in all_projectnames_map.Values)
				{
					if (!String.IsNullOrEmpty(proj.PDBFileName) )
					{
						sw.WriteLine("{0}", proj.PDBFileName);
					}
				}
			}
		}

		public void WriteDGML(String directory, string filename)
		{
			XmlDocument doc = new XmlDocument();
			var decl = doc.CreateXmlDeclaration("1.0", "utf-8", "no");
			doc.AppendChild(decl);

			var root = doc.CreateElement("DirectedGraph","http://schemas.microsoft.com/vs/2009/dgml");
			doc.AppendChild(root);

			var nodes = doc.CreateElement("Nodes", doc.DocumentElement.NamespaceURI);
			root.AppendChild(nodes);
			var links = doc.CreateElement("Links", doc.DocumentElement.NamespaceURI);
			root.AppendChild(links);
			var styles = doc.CreateElement("Styles", doc.DocumentElement.NamespaceURI);
			root.AppendChild(styles);
			var categories = doc.CreateElement("Categories", doc.DocumentElement.NamespaceURI);
			root.AppendChild(categories);
			foreach (ProjectBase proj in all_projectnames_map.Values)
			{
				UsedByCount.Add(proj,0);
			}
			foreach (ProjectBase proj in all_projectnames_map.Values)
			{
				ProjectBase[] dependents = proj.GetDependencies();
				foreach(ProjectBase dependent in dependents)
				{
					ushort val = UsedByCount[dependent];
					val++;
					UsedByCount[dependent] = val;
				}
			}

			foreach (ProjectBase proj in all_projectnames_map.Values)
			{
				var node = doc.CreateElement("Node", doc.DocumentElement.NamespaceURI);
				String thisProject = Path.GetFileName(proj.FullPath);
				node.SetAttribute("Id", thisProject );
				node.SetAttribute("FullPath", proj.FullPath);
				node.SetAttribute("OutputBinary", proj.OutputPath);
				String count = String.Format("{0}", UsedByCount[proj]);
				node.SetAttribute("UsedByCount", count);
				nodes.AppendChild(node);

				// We will not create links if nothing depends on it
				//if (UsedByCount[proj] == 0)
				//	continue;

				if (proj.GetDependencies().Length > 0)
				{
					foreach (ProjectBase dependency in proj.GetDependencies())
					{
						var link = doc.CreateElement("Link", doc.DocumentElement.NamespaceURI);
						link.SetAttribute("Source", thisProject);
						link.SetAttribute("Target", Path.GetFileName(dependency.FullPath));
						links.AppendChild(link);
					}
				}
			}

			doc.Save(Path.Combine(directory, filename + ".dgml"));
		}

		public void PrintProjectReferences()
		{
			foreach (ProjectBase proj in all_vc_projects)
			{
				if (proj.IsManaged)
				{
					int total_dependencies = proj.GetDependencies().Length;
					int total_managed_references = 0;
					if (proj.GetReferenceAssemblies() != null)
					{
						total_managed_references = proj.GetReferenceAssemblies().Length;
					}
					
					Console.WriteLine("{0} - Total: {1} Managed:{2}", proj.FullPath, total_dependencies, total_managed_references);
				}
			}
		}

		/// <summary>
		/// Instead of specifying dependencies in the solution file.
		/// Specify them as <ProjectReference> in the project itself.
		/// It is important to not specify a reference to an assembly twice. Therefore
		/// this will look for pre-existing references to assemblies in the solution file
		/// and remove them, before re-adding them as ProjectReferences
		/// </summary>
		public void WriteProjectReferences()
		{
			var nameLookups = all_projectnames_map.ToDictionary(p => p.Value.GetPropertyValue("AssemblyName"), p => p.Value, StringComparer.OrdinalIgnoreCase);

			foreach (ProjectBase proj in all_projectnames_map.Values)
			{
				// First remove any existing references to these projects since they will be
				// replaced by ProjectReferences
				var toBeRemoved = new List<ProjectItem>();
				var parents = new HashSet<ProjectElementContainer>();
				foreach(ProjectItem normalRef in proj.GetItems("Reference"))
				{
					var assemblyInclude = Utils.GetAssemblyName(normalRef.EvaluatedInclude);
					if (nameLookups.ContainsKey(assemblyInclude))
					{
						// Need to remove it
						toBeRemoved.Add(normalRef);
					}
				}
				foreach(var item in toBeRemoved)
				{
					var p = item.Xml.Parent;
					p.RemoveChild(item.Xml);
					if (parents.Contains(p) == false)
						parents.Add(p);
				}

				// Re-Add all Project References again
				if (proj.GetDependencies().Count() > 0)
				{
					var firstParent = parents.First();
					ProjectItemGroupElement ref_group = firstParent as ProjectItemGroupElement;
					foreach (ProjectBase dependency in proj.GetDependencies())
					{
						ProjectItemElement item = ref_group.AddItem("ProjectReference", Utils.PathRelativeTo(proj.FullPath, dependency.FullPath));
						item.AddMetadata("Project", dependency.GetPropertyValue("ProjectGuid"));
						item.AddMetadata("Name",    dependency.GetPropertyValue("AssemblyName"));
					}
				}
				proj.Save();
			}
		}

		// ================================================================================
		// ================================================================================

		/// <summary>
		/// Writes out a solution file with all the dependencies listed as well
		/// It only creates it for one configuration and platform.
		/// </summary>
		/// <param name="solution_file">The full path to the file to create. 
		/// If one exists already it will be overwritten</param>
		/// <param name="use_project_references">If false, the dependencies will be written out in the solution
		/// file itself. If true, the dependencies will be specified using ProjectReferences in each of the project
		/// files themselves.</param>
		public void WriteSolution(String solution_file, bool use_project_references)
		{
			using (StreamWriter sw = new StreamWriter(solution_file))
			{
				sw.WriteLine("Microsoft Visual Studio Solution File, Format Version 12.00");
				Guid solution_guid = Guid.NewGuid();
				String str_guid = solution_guid.ToString().ToUpper();

				var build_projects = from proj in build_products
							   orderby proj.FullPath ascending
							   select proj;

				foreach (ProjectBase proj in build_projects)
				{
					WriteBasicProjectData(sw, str_guid, proj, use_project_references);
				}
				sw.WriteLine("Global");
				sw.WriteLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
				sw.WriteLine("\t\t{0}|{1} = {0}|{1}", configuration, platform);
				sw.WriteLine("\tEndGlobalSection");
				sw.WriteLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
				foreach (ProjectBase proj in build_projects)
				{
					sw.WriteLine("\t\t{0}.{1}.ActiveCfg = {1}", proj.GetPropertyValue("ProjectGuid"), config_platform);
					sw.WriteLine("\t\t{0}.{1}.Build.0 = {1}", proj.GetPropertyValue("ProjectGuid"), config_platform);
				}
				sw.WriteLine("\tEndGlobalSection");
				sw.WriteLine("\tGlobalSection(ExtensibilityGlobals) = postSolution");
				sw.WriteLine("\tEndGlobalSection");
				sw.WriteLine("\tGlobalSection(ExtensibilityAddIns) = postSolution");
				sw.WriteLine("\tEndGlobalSection");
				sw.WriteLine("EndGlobal");
			}
			// last of all specify the project references
			if (use_project_references)
				WriteProjectReferences();
		}

		private void WriteBasicProjectData(StreamWriter sw, String str_guid, ProjectBase proj, bool use_project_references)
		{
			sw.WriteLine("Project(\"{4}{0}{5}\") = \"{1}\", \"{2}\", \"{3}\"",
				str_guid,
				proj.GetPropertyValue("ProjectName"),
				proj.FullPath,
				proj.GetPropertyValue("ProjectGuid"),
				'{', '}'
				);
			sw.WriteLine("\tProjectSection(ProjectDependencies) = postProject");
			if (!use_project_references)
				WriteDependencies(sw, proj);
			sw.WriteLine("\tEndProjectSection");
			sw.WriteLine("EndProject");
		}

		private void WriteDependencies(StreamWriter sw, ProjectBase project)
		{
			foreach (Project proj in project.GetDependencies())
			{
				sw.WriteLine("\t\t{0} = {0}", proj.GetPropertyValue("ProjectGuid"));
			}
		}

	}
}


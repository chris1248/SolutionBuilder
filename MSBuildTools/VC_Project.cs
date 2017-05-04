using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MSBuildTools
{
	/// <summary>
	/// Represents a .vcxproj file and provides methods to extract useful data from it.
	/// </summary>
	public class VC_Project : ProjectBase
	{
		public VC_Project(FileInfo file, Dictionary<String,String> global_properties)
			: base(file.FullName, global_properties)
		{
			GatherNativeInputsAndOutputs();
			GatherIdlDependencies();
			GatherManagedDependencies();
		}
		private String m_extension;
		/// <summary>
		/// The file extension of the output binary file that this project creates.
		/// </summary>
		public System.String Extension
		{
			get { return m_extension; }
		}
		private String m_import_library;
		/// <summary>
		/// Gets the lowercase, short file name of the library file (*.lib) that is created by this project
		/// </summary>
		public String ImportLibrary { get { return m_import_library; } }
		/// <summary>
		/// The Full path to any .lib files that this project creates. In Lowercase.
		/// </summary>
		private String m_Import_Library_FullPath;
		public String ImportLibraryFullPath { get { return m_Import_Library_FullPath; } }

		private String m_output_binary;
		/// <summary>
		/// Gets the lowercase, short file path of the binary that the linker generates. This is a virtual property.
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
		public override String PDBFileName { get { return m_pdb_filename;  } }

		private List<String> m_input_libraries = new List<string>();
		/// <summary>
		/// Gets a list of the native libraries this project depends on.
		/// Each String in the returned array is the lowercase, short, simple filename of the library it depends on.
		/// This code will not attempt to actually resolve the library file with actual libraries on the disk drive
		/// </summary>
		public String[] InputLibraries { get { return m_input_libraries.ToArray(); } }

		private String m_typelibrary;
		/// <summary>
		/// Some projects use the Midl compiler to generate archaric, old fashioned type libraries. 
		/// </summary>
		public String Typelibrary { get { return m_typelibrary; } }

		/// <summary>
		/// Type library files *.tlb that this project depends on. This is found by a multi-threaded brute force approach of
		/// parsing code files to find #import statements.
		/// </summary>
		private ConcurrentBag<String> input_type_libraries = new ConcurrentBag<String>();
		/// <summary>
		/// Gets Type Library files (*.tlb) that this project depends on
		/// </summary>
		public String[] InputTypeLibraries { get { return input_type_libraries.ToArray(); } }

		// ==========================================================================================

		/// <summary>
		/// Extract input and output properties from MSBuild.
		/// </summary>
		private void GatherNativeInputsAndOutputs()
		{
			m_extension = this.GetPropertyValue("TargetExt");
			//Debug.Assert(String.IsNullOrEmpty(m_extension) == false);
			m_output_binary = (this.GetPropertyValue("TargetName") + m_extension).ToLower();
			//Debug.Assert(String.IsNullOrEmpty(m_extension) == false);
			m_output_binary_path = this.GetPropertyValue("TargetPath");

			ProjectItemDefinition LinkDef = null;
			if (String.Compare(".lib", m_extension,true) == 0)
				LinkDef = this.ItemDefinitions["Lib"];
			else
				LinkDef = this.ItemDefinitions["Link"];
			
			//Debug.Assert(LinkDef != null);
			if (LinkDef != null)
			{
				String importLibPath = LinkDef.GetMetadataValue("ImportLibrary");
				//Debug.Assert(importLibPath != null);
				if (String.IsNullOrEmpty(importLibPath) == false)
					m_Import_Library_FullPath = importLibPath.ToLower();
				m_import_library = Path.GetFileName(importLibPath).ToLower();
				if (String.Compare(".lib", m_extension, true) == 0)
					m_import_library = m_output_binary;

				String additionalDependencies = LinkDef.GetMetadataValue("AdditionalDependencies");
				//Debug.Assert(additionalDependencies != null);
				ResolveInputLibraries(additionalDependencies);

				String PDB = LinkDef.GetMetadataValue("ProgramDatabaseFile");
				//Debug.Assert(PDB != null);
				m_pdb_filename = Path.GetFileName(PDB).ToLower();
			}

			ProjectItemDefinition MidlDef = this.ItemDefinitions["Midl"];
			if (MidlDef != null)
			{
				ProjectMetadata metaTypeLibraryName = MidlDef.GetMetadata("TypeLibraryName");
				if (metaTypeLibraryName.IsImported == false)
				{
					String typeLibrary = metaTypeLibraryName.EvaluatedValue;
					String tname = Path.GetFileName(this.ExpandString(typeLibrary)).ToLower();
					m_typelibrary = tname;
				}
			}
		}

		private void GatherIdlDependencies()
		{
			// Native projects can output .tlb files which are type libraries in addition to their libraries (.lib).
			// Therefore we have to go through this mess of looking for a .idl file. If a project has an .idl file than it will
			// generate a .tlb file. Confused already? 
			var idl_files = GetItems("Midl");

			List<ProjectItem> custom_idls = (from item in GetItems("CustomBuild")
								where item.EvaluatedInclude.Contains(".idl")
								select item).ToList();

			if ((idl_files.Count == 0) && (custom_idls.Count == 0))
			{
				// ALL of our .vcxproj projects have Midl item group definitions and hence specify type library names. 
				// This is because of imported/default property sheets that come from Microsoft.
				// However hardly any of them actually generate one. 
				// This is because hardly any of our projects actually process specify idl files.
				// So to cut down on clutter, only keep the type library names for projects that 
				// actually do generate type libraries. Since the idl_file file count is zero, 
				// this project does NOT generate a .tlb file
				// Debug.Assert(m_typelibrary == null);
				m_typelibrary = null;
			}

			
			// However just because this project doesn't generate a .tlb file doesn't mean it 
			// can't depend on one.
			// Now we have to search through every single cpp file looking for #import foo.tlb which is a big performance drain
			var cpp_files = GetItems("CLCompile").ToList();
			var h_files = GetItems("ClInclude").ToList();
			List<ProjectItem> all_files = new List<ProjectItem>();
			all_files.AddRange(cpp_files);
			all_files.AddRange(h_files);

			if (all_files.Count > 0)
			{
				//Parallel.ForEach(all_files, (ProjectItem item) =>
				//{
				//	String file_name = Path.GetFullPath(Path.Combine(this.DirectoryPath, item.EvaluatedInclude));
				//	if (File.Exists(file_name))
				//	{
				//		ParseNativeFile(file_name);
				//	}
				//});

				foreach (ProjectItem item in all_files)
				{
					String file_name = Path.GetFullPath(Path.Combine(this.DirectoryPath, item.EvaluatedInclude));
					if (File.Exists(file_name))
					{
						ParseNativeFile(file_name);
					}
				}
			}
			
		}
		

		// Used to look for managed c++ to find using declarations which reference managed assemblies.
		static Regex usingrx = new Regex("^(:b)*#(:b)*using.*<(.*)>.*$", RegexOptions.Compiled);
		// Use to look for Type Libraries that are generated by the Midl compiler (COM).
		// Explanation
		// ^                         -- Start of the line
		// (\\s)*#(\\s)*             -- White space before the #, a sharp sign, and white space after it
		// import(\\s)*              -- the keyword import, and any amount of whitespace after it
		// \"([^\"]*\\w)*\"          -- Find text inside of quote, the string can contain any number of quotes, but this returns the first one.
		// (.*)$                     -- Any character until the end of the string
		Regex importrx = new Regex("^(\\s)*#(\\s)*import(\\s)*\"([^\"]*\\w)*\"(.*)$", RegexOptions.Compiled);

		private void GatherManagedDependencies()
		{
			// There are three ways to see if a .vcxproj is a managed assembly

			// Rule 1: The first is to see if there are reference assemblies.
			bool has_refs = base.GatherReferenceAssemblies() > 0;
			
			// Rule 2: The second is to see if it has a CLRSupport property set to true
			ProjectProperty clr_support = GetProperty("CLRSupport");
			
			// Rule 3: The third is cpp and header files with the <CompileAsManaged> element set to true.
			var managed_cpp_files = from item in this.GetItems("CLCompile")
								where item.GetMetadataValue("CompileAsManaged") == "true" // If a file is compiled with the /clr switch, it will have an xml element as follows
								select item;

			if (!has_refs &&								// Rule 1: Doesn't have reference items
				(clr_support != null && clr_support.EvaluatedValue == "false") &&		// Rule 2: CLR switch is NOT turned on
				managed_cpp_files.Count() == 0				// Rule 3: No files with the managed switch turned on
				)
			{
				// This project is NOT a managed project
				// bail out early
				return;
			}

			is_managed = true;
			foreach (var entry in managed_cpp_files)
			{
				String cpp_file = Path.Combine(this.DirectoryPath, entry.EvaluatedInclude);
				if (File.Exists(cpp_file))
				{
					// read through the file looking for #using statements
					ParseManagedFile(cpp_file);
				}
			}

			// Header files have using statements as well, so we have to check all header files.
			// Note we have to check ALL header files, unlike .cpp files above.
			var managed_header_files = GetItems("ClInclude");

			foreach (var entry in managed_header_files)
			{
				String cpp_file = Path.Combine(this.DirectoryPath, entry.EvaluatedInclude);
				if (File.Exists(cpp_file))
				{
					// read through the file looking for #using statements
					ParseManagedFile(cpp_file);
				}
			}

			// Might as well check that it uses .NET 4.0 and not some other version
			String netversion = GetPropertyValue("TargetFrameworkVersion");
			bool ignore_case = true;
			if (String.Compare("V4.0", netversion, ignore_case) != 0)
			{
				Console.WriteLine("Warning: Wrong version of .NET framework targeted in {0}, should be V4.0", this.FullPath);
			}

		}

		/// <summary>
		/// This parses a native c++ file looking for dependencies that arise
		/// from #import statements (for type libraries)
		/// </summary>
		/// <param name="code_file">The full path to the code file to parse</param>
		private void ParseNativeFile(String code_file)
		{
			using (StreamReader tr = new StreamReader(code_file))
			{
				String line = null;
				while ((line = tr.ReadLine()) != null)
				{
					// Use our handy dandy regular expression engine to look for an assembly
					Match match = importrx.Match(line);
					if (match.Success)
					{
						int count = match.Groups.Count;

						String import_statement = match.Groups[4].Value;
						String import_file_name = Path.GetFileName(import_statement).ToLower();
						if (!input_type_libraries.Contains(import_file_name))
						{
							input_type_libraries.Add(import_file_name);
						}
					}
				}
			}
		}

		private void ParseManagedFile(String code_file)
		{
			using (StreamReader tr = new StreamReader(code_file))
			{
				String line = null;
				while ((line = tr.ReadLine()) != null)
				{
					// Use our handy dandy regular expression engine to look for an assembly
					Match match = usingrx.Match(line);
					if (match.Success)
					{
						int count = match.Groups.Count;

						// It's always the last one, due to how the regex is written.
						// If the Regex changes, this will have to change too
						String assembly_ref = match.Groups[count - 1].Value.ToLower();

						// All Assembly references are stored without the .dll file suffix
						// assembly_ref = Path.GetFileNameWithoutExtension(assembly_ref);

						base.AddReferenceAssembly(assembly_ref);
					}
				}
			}
		}

		private void ResolveInputLibraries(String additionalDependencies)
		{
			String[] splits = additionalDependencies.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			if (splits.Length >= 1)
			{
				foreach (String lib in splits)
				{
					if (lib.StartsWith("%"))
					{
						continue;
					}
					String expanded_lib = ExpandString(lib).Trim();
					String lib_filename = Path.GetFileName(expanded_lib).ToLower();
					m_input_libraries.Add(lib_filename);
				}
			}
		}

		public override string ToString()
		{
			return "{" + Path.GetFileName(FullPath) + "}";
		}
	}
}

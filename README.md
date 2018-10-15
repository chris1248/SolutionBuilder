# SolutionBuilder
Generates a solution file by pointing to a directory

# Build Status
[![Build Status](https://dev.azure.com/christopher3d/Chris%20OpenSource%20Github%20Projects/_apis/build/status/SolutionBuilder2)](https://dev.azure.com/christopher3d/Chris%20OpenSource%20Github%20Projects/_build/latest?definitionId=3)

# Introduction
This is used to quickly generate a visual studio solution file for many msbuild project files in a directory.
The impetus being that there are many old projects with very large builds spanning hundreds of projects to build.
Many old build systems manually specified by hand the build order of these projects. What compounded the problem
is these old build systems were written from the ground up in a totally ad-hoc manner in systems that were foreign
to the target platform. In my experience these build systems are fragile, and usually not very fast.

Building hundreds of projects is not hard, even to do in parallel. The key point is to make sure that MSBuild knows
which projects depend on each other. Once the dependencies are figured out, MSBuild is very quick (on a multi-core computer)
to build.

A visual studio solution file is simply a list of files to build, along with a listing of which projects depend on each other.
It uses an absolutely opaque format, but until Microsoft see's fit to update it, we are stuck with it.

This project takes care inspecting existing projects (*.csproj, *.vcxproj), figuring out their dependencies and then
outputing those projects and their dependencies to the solution file.

# How to use it

This supports two different ways to specify projects to include in the solution file.
1. Include everything in a directory. Use this if your source is clean, in that all project files are 
part of the build or product, and there are no orphaned, abandoned projects.
2. Search in a directory, but limit the projects to a list of files specified in a msbuild file. This is
necessary if there are lots of old, orphaned, abandoned projects that are NOT part of the build. 

## Option 1
Run it with the following parameters:
`SolutionBuilder <directory> <output solution file> <configuration> <platform>`

   * `<directory>` - The directory to recursively search in. This searches for *.vcxproj and *.csproj files
   * `<output solution file>` - The file path for a *.sln file you want this tool to generate
   * `<configuration>` - The build configuration (Debug, release)
   * `<platform>` - The build platform (AnyCPU, x86, x64, Win32 etc...)

## Option 2
Run it with the following parameters:
`SolutionBuilder <directory> <output solution file> <configuration> <platform> <msbuild file> <ItemName>`

   * `<directory>` - The directory to recursively search in. This searches for *.vcxproj and *.csproj files
   * `<output solution file>` - The file path for a *.sln file you want this tool to generate
   * `<configuration>` - The build configuration (Debug, release)
   * `<platform>` - The build platform (AnyCPU, x86, x64, Win32 etc...)
   * `<msbuild file>` - A valid msbuild file with one item that specifies the cannonical list of projects in the build
   * `<ItemName>` - The name of the item in the build file, that specifies the cannonical list of projects in the build

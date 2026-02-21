using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("OrbitalKeeper")]
[assembly: AssemblyDescription("Orbital station-keeping mod for KSP, counteracting orbital decay.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("OrbitalKeeper")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: Guid("3e4ab7af-6c35-43bd-8717-b9144e113f65")]

[assembly: AssemblyVersion("2.1.1")]
[assembly: AssemblyFileVersion("2.1.1")]

// Ensure ClickThroughBlocker loads before OrbitalKeeper (fixes UI click-through)
[assembly: KSPAssemblyDependency("ClickThroughBlocker", 1, 0)]

using System;
using System.CodeDom.Compiler;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace HttpFileServer
{
    public static class CompilationHelper
    {
        public static Assembly Compile(String sourceName)
        {
            sourceName = sourceName.Replace("/", "\\");
            var sourceFile = new FileInfo(sourceName);
            CodeDomProvider provider = null;

            // Select the code provider based on the input file extension. 
            if (sourceFile.Extension.ToUpper(CultureInfo.InvariantCulture) == ".CS")
                provider = CodeDomProvider.CreateProvider("CSharp");
            else if (sourceFile.Extension.ToUpper(CultureInfo.InvariantCulture) == ".VB")
                provider = CodeDomProvider.CreateProvider("VisualBasic");
            else
                Console.WriteLine("Source file must have a .cs or .vb extension");

            if (provider != null)
            {
                var options = new CompilerParameters
                    {
                        GenerateExecutable = false,
                        GenerateInMemory = true,
                        TreatWarningsAsErrors = false,
                        ReferencedAssemblies =
                            {
                                //"System.dll",
                                //"System.Configuration.dll",
                                //"System.Core.dll",
                                //"System.Data.dll",
                                //"System.Data.DataSetExtensions.dll",
                                //"System.Drawing.dll",
                                //"System.Xml.dll",
                                //"System.Xml.Linq.dll",
                                Assembly.GetAssembly(typeof(MyHttpServer)).CodeBase.Replace("file:///", ""),
                            }
                    };

                // Invoke compilation of the source file.
                var result = provider.CompileAssemblyFromFile(options, sourceName);

                if (result.Errors.Count > 0)
                {
                    // Display compilation errors.
                    Console.WriteLine(
                        "Errors building {0} into {1}",
                        sourceName,
                        result.PathToAssembly);
                    foreach (CompilerError ce in result.Errors)
                    {
                        Console.WriteLine("  {0}", ce.ToString());
                        Console.WriteLine();
                    }
                }
                else
                {
                    // Display a successful compilation message.
                    Console.WriteLine(
                        "Source {0} built into {1} successfully.",
                        sourceName,
                        result.PathToAssembly);
                }

                // Return the results of the compilation. 
                return result.CompiledAssembly;
            }

            return null;
        }
    }
}
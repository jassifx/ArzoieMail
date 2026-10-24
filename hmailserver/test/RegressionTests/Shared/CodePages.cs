using System.Text;
using NUnit.Framework;

// Outside any namespace, so that it runs once before every test in the assembly.

/// <summary>
/// .NET only carries the Unicode encodings and ASCII/Latin-1 by default. The tests build messages
/// in Windows-1252 and other legacy code pages, which need the code pages provider.
/// </summary>
[SetUpFixture]
public class RegressionTestsCodePages
{
   [OneTimeSetUp]
   public void Register()
   {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
   }
}

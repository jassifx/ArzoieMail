using System.Diagnostics;

namespace hMailServer.Shared
{
   /// <summary>
   /// Opens a URL or a document with its associated program. On .NET, Process.Start no longer
   /// goes through the shell by default, so a URL passed to it fails instead of opening the
   /// browser.
   /// </summary>
   public static class ShellLauncher
   {
      public static void Open(string target)
      {
         using (Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }))
         {
         }
      }
   }
}

using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace RegressionTests.Infrastructure
{
   /// <summary>
   /// The server's executables are built with ASLR, DEP, Control Flow Guard and CET shadow
   /// stack compatibility. These read the installed files' PE headers to confirm the linker
   /// actually marked them, since a dropped flag would build and run just the same.
   /// </summary>
   [TestFixture]
   public class BinaryHardening : TestFixtureBase
   {
      private const ushort DynamicBase = 0x0040;
      private const ushort HighEntropyVa = 0x0020;
      private const ushort NxCompat = 0x0100;
      private const ushort GuardCf = 0x4000;

      private const int DebugTypeExDllCharacteristics = 20;
      private const uint ExDllCharacteristicsCetCompat = 0x0001;

      private static string GetBinFile(string fileName)
      {
         var processes = Process.GetProcessesByName("hmailserver");
         Assert.AreEqual(1, processes.Length, "Expected exactly one running hMailServer.exe.");

         return Path.Combine(Path.GetDirectoryName(processes[0].MainModule.FileName), fileName);
      }

      private class PeImage
      {
         public ushort DllCharacteristics;
         public uint ExDllCharacteristics;
      }

      private static PeImage ReadPeImage(string path)
      {
         var data = File.ReadAllBytes(path);

         int peOffset = BitConverter.ToInt32(data, 0x3C);
         Assert.AreEqual(0x00004550, BitConverter.ToUInt32(data, peOffset), "PE signature");

         int coffOffset = peOffset + 4;
         int numberOfSections = BitConverter.ToUInt16(data, coffOffset + 2);
         int sizeOfOptionalHeader = BitConverter.ToUInt16(data, coffOffset + 16);

         int optionalOffset = coffOffset + 20;
         Assert.AreEqual(0x20B, BitConverter.ToUInt16(data, optionalOffset), "PE32+ (x64) optional header");

         var image = new PeImage { DllCharacteristics = BitConverter.ToUInt16(data, optionalOffset + 70) };

         // Data directory 6 is the debug directory; the directories start at offset 112 in PE32+.
         int debugDirectoryRva = BitConverter.ToInt32(data, optionalOffset + 112 + 6 * 8);
         int debugDirectorySize = BitConverter.ToInt32(data, optionalOffset + 112 + 6 * 8 + 4);

         int sectionsOffset = optionalOffset + sizeOfOptionalHeader;
         int debugDirectoryOffset = -1;
         for (int i = 0; i < numberOfSections; i++)
         {
            int section = sectionsOffset + i * 40;
            int virtualSize = BitConverter.ToInt32(data, section + 8);
            int virtualAddress = BitConverter.ToInt32(data, section + 12);
            int pointerToRawData = BitConverter.ToInt32(data, section + 20);

            if (debugDirectoryRva >= virtualAddress && debugDirectoryRva < virtualAddress + virtualSize)
               debugDirectoryOffset = debugDirectoryRva - virtualAddress + pointerToRawData;
         }

         for (int entry = 0; debugDirectoryOffset >= 0 && entry < debugDirectorySize / 28; entry++)
         {
            int entryOffset = debugDirectoryOffset + entry * 28;
            if (BitConverter.ToInt32(data, entryOffset + 12) != DebugTypeExDllCharacteristics)
               continue;

            int pointerToRawData = BitConverter.ToInt32(data, entryOffset + 24);
            image.ExDllCharacteristics = BitConverter.ToUInt32(data, pointerToRawData);
         }

         return image;
      }

      [TestCase("hMailServer.exe")]
      [TestCase("hMailServer.Minidump.exe")]
      [Description("The executable is marked for ASLR, high-entropy ASLR and DEP")]
      public void ExecutableShouldUseAslrAndDep(string fileName)
      {
         var image = ReadPeImage(GetBinFile(fileName));

         Assert.AreEqual(DynamicBase, image.DllCharacteristics & DynamicBase, "DYNAMIC_BASE (ASLR)");
         Assert.AreEqual(HighEntropyVa, image.DllCharacteristics & HighEntropyVa, "HIGH_ENTROPY_VA");
         Assert.AreEqual(NxCompat, image.DllCharacteristics & NxCompat, "NX_COMPAT (DEP)");
      }

      [TestCase("hMailServer.exe")]
      [TestCase("hMailServer.Minidump.exe")]
      [Description("The executable is built with Control Flow Guard")]
      public void ExecutableShouldUseControlFlowGuard(string fileName)
      {
         var image = ReadPeImage(GetBinFile(fileName));

         Assert.AreEqual(GuardCf, image.DllCharacteristics & GuardCf, "GUARD_CF");
      }

      [TestCase("hMailServer.exe")]
      [TestCase("hMailServer.Minidump.exe")]
      [Description("The executable is marked compatible with CET shadow stacks")]
      public void ExecutableShouldBeCetCompatible(string fileName)
      {
         var image = ReadPeImage(GetBinFile(fileName));

         Assert.AreEqual(ExDllCharacteristicsCetCompat, image.ExDllCharacteristics & ExDllCharacteristicsCetCompat, "CET_COMPAT");
      }
   }
}

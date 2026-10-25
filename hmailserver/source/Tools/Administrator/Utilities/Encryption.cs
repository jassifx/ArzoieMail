// Copyright (c) 2010 Martin Knafve / hMailServer.com.
// http://www.hmailserver.com

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace hMailServer.Administrator.Utilities
{
    // The passwords the Administrator saves for its server connections, in hMailAdmin.exe.config.
    //
    // They used to be AES-encrypted under a key derived from THIS_KEY_IS_NOT_SECRET, which is in the
    // source code, so anyone with the file could read them. They are protected with DPAPI now,
    // for the Windows user who saved them: only that user, on this computer, can unprotect them.
    // A value saved the old way is still read, and UserSettings protects it again when it loads.
    internal class Encryption
    {
        // dpapi1:<base64 of the CryptProtectData blob>
        private const string Prefix = "dpapi1:";

        // Mixed into the protection, so that the blob unprotects only for this purpose.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ArzoieMail Administrator saved password, version 1");

        private static string NOT_SECRET_KEY = "THIS_KEY_IS_NOT_SECRET";

        public static string Encrypt(string plainText)
        {
            byte[] plain = Encoding.UTF8.GetBytes(plainText);

            try
            {
                return Prefix + Convert.ToBase64String(Dpapi(true, plain));
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public static string Decrypt(string encryptedText)
        {
            if (!IsSavedTheOldWay(encryptedText))
            {
                byte[] plain = Dpapi(false, Convert.FromBase64String(encryptedText.Substring(Prefix.Length)));

                try
                {
                    return Encoding.UTF8.GetString(plain);
                }
                finally
                {
                    Array.Clear(plain, 0, plain.Length);
                }
            }

            return DecryptOldWay(encryptedText);
        }

        // True for a value saved by an earlier version, which UserSettings protects again.
        public static bool IsSavedTheOldWay(string encryptedText)
        {
            return !string.IsNullOrEmpty(encryptedText) && !encryptedText.StartsWith(Prefix, StringComparison.Ordinal);
        }

        private static string DecryptOldWay(string encryptedText)
        {
            // Encryption operates on byte arrays, not on strings.
            byte[] cipherBytes = Convert.FromBase64String(encryptedText);

            // Derive a key from the password.
            PasswordDeriveBytes pdb = new PasswordDeriveBytes(NOT_SECRET_KEY,
                new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });

            // AES, which is what Rijndael with its default 128-bit block always was.
            Aes rijndaelAlgorithm = Aes.Create();
            rijndaelAlgorithm.Key = pdb.GetBytes(32);
            rijndaelAlgorithm.IV = pdb.GetBytes(16);

            MemoryStream memoryStream = new MemoryStream();
            CryptoStream cryptoStream = new CryptoStream(memoryStream, rijndaelAlgorithm.CreateDecryptor(), CryptoStreamMode.Write);
            cryptoStream.Write(cipherBytes, 0, cipherBytes.Length);
            cryptoStream.Close();

            byte[] decryptedData = memoryStream.ToArray();

            return Encoding.Unicode.GetString(decryptedData);
        }

        // CryptProtectData directly rather than ProtectedData, which .NET only has in a package of
        // its own that the installer would have to ship as well.
        private static byte[] Dpapi(bool protect, byte[] input)
        {
            var inBlob = new DataBlob();
            var entropyBlob = new DataBlob();
            var outBlob = new DataBlob();

            GCHandle inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            GCHandle entropyHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);

            try
            {
                inBlob.cbData = input.Length;
                inBlob.pbData = inHandle.AddrOfPinnedObject();
                entropyBlob.cbData = Entropy.Length;
                entropyBlob.pbData = entropyHandle.AddrOfPinnedObject();

                bool done = protect
                    ? CryptProtectData(ref inBlob, "ArzoieMail Administrator saved password", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);

                if (!done)
                    throw new CryptographicException(Marshal.GetLastWin32Error());

                byte[] output = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, output, 0, outBlob.cbData);
                return output;
            }
            finally
            {
                inHandle.Free();
                entropyHandle.Free();

                if (outBlob.pbData != IntPtr.Zero)
                {
                    // Cleared before it is freed: it holds the password when unprotecting.
                    Marshal.Copy(new byte[outBlob.cbData], 0, outBlob.pbData, outBlob.cbData);
                    LocalFree(outBlob.pbData);
                }
            }
        }

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string szDataDescr, ref DataBlob pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}

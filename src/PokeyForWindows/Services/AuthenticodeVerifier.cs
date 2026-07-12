using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace PokeyForWindows;

internal static class AuthenticodeVerifier
{
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckChain = 0x00000040;

    private static readonly Guid GenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        internal uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        internal string FilePath = string.Empty;
        internal IntPtr FileHandle;
        internal IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public WinTrustData()
        {
        }

        internal uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        internal IntPtr PolicyCallbackData;
        internal IntPtr SipClientData;
        internal uint UiChoice = WtdUiNone;
        internal uint RevocationChecks = WtdRevokeWholeChain;
        internal uint UnionChoice = WtdChoiceFile;
        internal IntPtr FileInfo;
        internal uint StateAction = WtdStateActionVerify;
        internal IntPtr StateData;
        internal IntPtr UrlReference;
        internal uint ProviderFlags = WtdRevocationCheckChain;
        internal uint UiContext;
        internal IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        [In] ref Guid actionId,
        [In, Out] ref WinTrustData trustData);

    internal static void Verify(string filePath, string expectedSignerSubstring)
    {
        WinTrustFileInfo fileInfo = new()
        {
            FilePath = filePath
        };
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        WinTrustData trustData = new();
        Guid actionId = GenericVerifyV2;

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            trustData.FileInfo = fileInfoPointer;

            int result = WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);
            if (result != 0)
            {
                throw new InvalidDataException(
                    $"Authenticode verification failed with status 0x{result:X8}.");
            }

#pragma warning disable SYSLIB0057 // Required to extract an Authenticode signer from a PE file.
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using X509Certificate2 certificate2 = new(certificate);
            string signer = certificate2.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (!signer.Contains(expectedSignerSubstring, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The update signer '{signer}' does not match the expected publisher.");
            }
        }
        finally
        {
            if (trustData.StateData != IntPtr.Zero)
            {
                trustData.StateAction = WtdStateActionClose;
                _ = WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);
            }

            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }
}

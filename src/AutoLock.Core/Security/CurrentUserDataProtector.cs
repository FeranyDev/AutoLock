using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AutoLock.Core;

internal static class CurrentUserDataProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("AutoLock.BindingConfig.IRK.v1");

    public static string ProtectString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToBase64String(Protect(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string UnprotectString(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        byte[] ciphertext;
        try
        {
            ciphertext = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("The protected value is not valid Base64 data.", exception);
        }

        var plaintext = Unprotect(ciphertext);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] Protect(byte[] plaintext)
    {
        var input = CreateBlob(plaintext);
        var entropy = CreateBlob(OptionalEntropy);
        var output = default(DataBlob);

        try
        {
            if (!CryptProtectData(
                    ref input,
                    "AutoLock IRK",
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            return CopyBlob(output);
        }
        finally
        {
            FreeInputBlob(input);
            FreeInputBlob(entropy);
            FreeOutputBlob(output);
        }
    }

    private static byte[] Unprotect(byte[] ciphertext)
    {
        var input = CreateBlob(ciphertext);
        var entropy = CreateBlob(OptionalEntropy);
        var output = default(DataBlob);
        var description = IntPtr.Zero;

        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    out description,
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            return CopyBlob(output);
        }
        finally
        {
            FreeInputBlob(input);
            FreeInputBlob(entropy);
            FreeOutputBlob(output);
            if (description != IntPtr.Zero)
            {
                LocalFree(description);
            }
        }
    }

    private static DataBlob CreateBlob(byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob(data.Length, pointer);
    }

    private static byte[] CopyBlob(DataBlob blob)
    {
        if (blob.Size <= 0 || blob.Data == IntPtr.Zero)
        {
            return [];
        }

        var result = new byte[blob.Size];
        Marshal.Copy(blob.Data, result, 0, blob.Size);
        return result;
    }

    private static void FreeInputBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
        }
    }

    private static void FreeOutputBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            LocalFree(blob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob(int size, IntPtr data)
    {
        public readonly int Size = size;
        public readonly IntPtr Data = data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

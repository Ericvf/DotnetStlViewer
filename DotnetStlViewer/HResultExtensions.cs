﻿using Silk.NET.Core.Native;
using System.Runtime.InteropServices;
using System.Text;

public static unsafe class HResultExtensions
{
    public static Guid* Pointer(this Guid guid) => &guid;

    public static void ThrowHResult(this int hResult)
    {
        StringBuilder msgOut = new StringBuilder(256);
        int size = FormatMessage(FORMAT_MESSAGE.ALLOCATE_BUFFER | FORMAT_MESSAGE.FROM_SYSTEM | FORMAT_MESSAGE.IGNORE_INSERTS,
                      IntPtr.Zero, hResult, 0, out msgOut, msgOut.Capacity, IntPtr.Zero);
        
        try
        {
            SilkMarshal.ThrowHResult(hResult);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.ToString() + msgOut.ToString()); 
        }
    }

    [DllImport("kernel32.dll")]
    static extern int FormatMessage(
        FORMAT_MESSAGE dwFlags,
        IntPtr lpSource,
        int dwMessageId,
        uint dwLanguageId,
        out StringBuilder msgOut,
        int nSize,
        IntPtr Arguments
    );

    enum FORMAT_MESSAGE : uint
    {
        ALLOCATE_BUFFER = 0x00000100,
        IGNORE_INSERTS = 0x00000200,
        FROM_SYSTEM = 0x00001000,
        ARGUMENT_ARRAY = 0x00002000,
        FROM_HMODULE = 0x00000800,
        FROM_STRING = 0x00000400
    }
}
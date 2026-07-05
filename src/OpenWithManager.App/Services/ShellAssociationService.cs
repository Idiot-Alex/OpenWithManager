using System.Runtime.InteropServices;
using OpenWithManager.App.Models;

namespace OpenWithManager.App.Services;

public sealed class ShellAssociationService
{
    public List<FormatAppCandidate> GetHandlers(string extension)
    {
        var candidates = new List<FormatAppCandidate>();
        candidates.AddRange(ReadHandlers(extension, AssocFilter.Recommended, "ShellRecommended"));
        candidates.AddRange(ReadHandlers(extension, AssocFilter.None, "ShellHandler"));
        return candidates;
    }

    public void MakeDefault(string extension, string shellHandlerId, string appName)
    {
        var handler = FindHandler(extension, shellHandlerId)
            ?? throw new InvalidOperationException("The selected app is no longer available for this format.");

        var result = handler.MakeDefault(appName);
        Marshal.ReleaseComObject(handler);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static IEnumerable<FormatAppCandidate> ReadHandlers(
        string extension,
        AssocFilter filter,
        string source)
    {
        var result = SHAssocEnumHandlers(extension, filter, out var handlers);
        if (result < 0 || handlers is null)
        {
            yield break;
        }

        try
        {
            while (handlers.Next(1, out var handler, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    var appName = ReadComString(handler.GetUIName) ?? ReadComString(handler.GetName);
                    var handlerId = ReadComString(handler.GetName) ?? appName;
                    if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(handlerId))
                    {
                        continue;
                    }

                    yield return new FormatAppCandidate(
                        appName,
                        null,
                        source,
                        false,
                        ShellHandlerId: handlerId,
                        CanMakeDefault: true);
                }
                finally
                {
                    Marshal.ReleaseComObject(handler);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(handlers);
        }
    }

    private static IAssocHandler? FindHandler(string extension, string shellHandlerId)
    {
        var result = SHAssocEnumHandlers(extension, AssocFilter.None, out var handlers);
        if (result < 0 || handlers is null)
        {
            return null;
        }

        try
        {
            while (handlers.Next(1, out var handler, out var fetched) == 0 && fetched == 1)
            {
                var handlerId = ReadComString(handler.GetName);
                if (string.Equals(handlerId, shellHandlerId, StringComparison.OrdinalIgnoreCase))
                {
                    return handler;
                }

                Marshal.ReleaseComObject(handler);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(handlers);
        }

        return null;
    }

    private delegate int ReadComStringCallback(out IntPtr value);

    private static string? ReadComString(ReadComStringCallback read)
    {
        var result = read(out var value);
        if (result < 0 || value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHAssocEnumHandlers(
        string pszExtra,
        AssocFilter afFilter,
        [MarshalAs(UnmanagedType.Interface)]
        out IEnumAssocHandlers? ppEnumHandler);

    private enum AssocFilter
    {
        None = 0,
        Recommended = 1
    }

    [ComImport]
    [Guid("973810AE-9599-4B88-9E4D-6EE98C9552DA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumAssocHandlers
    {
        [PreserveSig]
        int Next(
            uint celt,
            [MarshalAs(UnmanagedType.Interface)] out IAssocHandler rgelt,
            out uint pceltFetched);
    }

    [ComImport]
    [Guid("F04061AC-1659-4A3F-A954-775AA57FC083")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAssocHandler
    {
        [PreserveSig]
        int GetName(out IntPtr ppsz);

        [PreserveSig]
        int GetUIName(out IntPtr ppsz);

        [PreserveSig]
        int GetIconLocation(out IntPtr ppszPath, out int pIndex);

        [PreserveSig]
        int IsRecommended();

        [PreserveSig]
        int MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string pszDescription);

        [PreserveSig]
        int Invoke(IntPtr pdo);

        [PreserveSig]
        int CreateInvoker(IntPtr pdo, out IntPtr ppInvoker);
    }
}

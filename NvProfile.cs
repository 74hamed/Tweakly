using System.Runtime.InteropServices;

namespace Tweakly;

// Minimal NVAPI DRS binding. Uses the NVIDIA driver already installed on the machine.
// Constants and ABI are documented by NVIDIA; no third-party executable is bundled.
public static class NvProfile
{
    private const int Size = 12320;
    private const int IdOffset = 4100, TypeOffset = 4104, PredefinedOffset = 4112, ValueOffset = 8220;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionCall(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProfileCall(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSetting(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSetting(IntPtr session, IntPtr profile, uint id);
    private sealed class Context : IDisposable
    {
        private IntPtr library;
        public IntPtr Session, Profile;
        private Query query;
        public Context()
        {
            library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));
            query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            try
            {
                Check(Function<Initialize>(0x0150E828)());
                Check(Function<Create>(0x0694D52E)(out Session));
                Check(Function<SessionCall>(0x375DBD6B)(Session));
                Check(Function<ProfileCall>(0xDA8466A0)(Session, out Profile));
            }
            catch { Dispose(); throw; }
        }
        public T Function<T>(uint id) where T : Delegate { var address = query(id); return address != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(address) : throw new NotSupportedException("NVAPI function is unavailable."); }
        public void Dispose() { if (Session != IntPtr.Zero) { Function<SessionCall>(0xDAD9CFF8)(Session); Session = IntPtr.Zero; } if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; } }
    }
    private static void Check(int status) { if (status != 0) throw new InvalidOperationException("NVIDIA driver returned NVAPI status " + status); }
    private static IntPtr Buffer(uint id)
    {
        var pointer = Marshal.AllocHGlobal(Size); Marshal.Copy(new byte[Size], 0, pointer, Size);
        Marshal.WriteInt32(pointer, Size | (1 << 16)); Marshal.WriteInt32(pointer, IdOffset, unchecked((int)id));
        return pointer;
    }
    public static Reading Read(uint id)
    {
        using var context = new Context(); var buffer = Buffer(id);
        try
        {
            var status = context.Function<GetSetting>(0x73BF8338)(context.Session, context.Profile, id, buffer);
            if (status == -160) return new(true, false, ""); // NVAPI_SETTING_NOT_FOUND
            Check(status);
            if (Marshal.ReadInt32(buffer, TypeOffset) != 0) return new(false, false, "", "Only DWORD driver settings are supported.");
            // A predefined value is restored by removing the custom override.
            return new(true, Marshal.ReadInt32(buffer, PredefinedOffset) == 0, unchecked((uint)Marshal.ReadInt32(buffer, ValueOffset)).ToString());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public static void Write(uint id, uint value, bool delete)
    {
        using var context = new Context();
        if (delete)
        {
            var result = context.Function<DeleteSetting>(0xE4A26362)(context.Session, context.Profile, id);
            if (result != -160) Check(result);
        }
        else
        {
            var buffer = Buffer(id);
            try { Marshal.WriteInt32(buffer, ValueOffset, unchecked((int)value)); Check(context.Function<SetSetting>(0x577DD202)(context.Session, context.Profile, buffer)); }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        Check(context.Function<SessionCall>(0xFCBC7E14)(context.Session));
    }
}

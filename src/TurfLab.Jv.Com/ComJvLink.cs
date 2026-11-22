using System.Reflection;
using TurfLab.Jv;

namespace TurfLab.Jv.Com;

/// <summary>
/// 本物の JV-Link（COM, ProgID "JVDTLab.JVLink"）を呼ぶ。
/// ・JV-Link は 32bit の部品なので、このアプリも 32bit(x86)でビルドして動かす。
/// ・JVRead は文字列バッファを「参照渡し」で受け取る。実機（PowerShell+C#）で SEHException を避けられた
///   ParameterModifier 経由の呼び方と、事前にバッファを確保する方法をそのまま踏襲した。
///   （docs/DATA_SOURCES.md の落とし穴 2 を参照）
/// </summary>
public sealed class ComJvLink : IJvLink
{
    private const int BufferChars = 120000;
    private readonly Type _type;
    private readonly object _obj;

    public ComJvLink()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("JV-Link は Windows でのみ動きます");
        if (IntPtr.Size != 4) throw new InvalidOperationException("JV-Link は32bit部品です。このアプリを32bit(x86)で動かしてください");
        _type = Type.GetTypeFromProgID("JVDTLab.JVLink")
                ?? throw new InvalidOperationException("JV-Link が見つかりません。JRA-VAN Data Lab. のソフトをインストールしてください");
        _obj = Activator.CreateInstance(_type)!;
    }

    private object? Call(string name, object?[] args, bool[]? byRef = null)
    {
        if (byRef is null) return _type.InvokeMember(name, BindingFlags.InvokeMethod, null, _obj, args);
        var pm = new ParameterModifier(args.Length);
        for (int i = 0; i < byRef.Length; i++) pm[i] = byRef[i];
        return _type.InvokeMember(name, BindingFlags.InvokeMethod, null, _obj, args, [pm], null, null);
    }

    public int Init(string softwareId) => (int)Call("JVInit", [softwareId])!;

    public JvOpenResult Open(string dataSpec, string fromTime, int option)
    {
        object?[] a = [dataSpec, fromTime, option, 0, 0, ""];
        int rc = (int)Call("JVOpen", a, [false, false, false, true, true, true])!;
        return new JvOpenResult(rc, Convert.ToInt32(a[3]), Convert.ToInt32(a[4]), Convert.ToString(a[5]) ?? "");
    }

    public int Status() => (int)Call("JVStatus", [])!;

    public JvReadResult Read()
    {
        object?[] a = [new string(' ', BufferChars), BufferChars, new string(' ', 512)];
        int rc = (int)Call("JVRead", a, [true, true, true])!;
        return new JvReadResult(rc, Convert.ToString(a[0]) ?? "", (Convert.ToString(a[2]) ?? "").Trim());
    }

    public void Close() => Call("JVClose", []);

    public void Dispose()
    {
        try { Close(); } catch { /* 無視 */ }
        if (OperatingSystem.IsWindows() && _obj is not null && System.Runtime.InteropServices.Marshal.IsComObject(_obj))
            System.Runtime.InteropServices.Marshal.ReleaseComObject(_obj);
    }
}

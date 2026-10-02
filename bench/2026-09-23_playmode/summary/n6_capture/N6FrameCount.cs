using System.Reflection;
using UnityEditor;
// N6 임시: Profiler Frame Count 변경. E 장 캡처 동안만 200, 끝나면 2000으로 되돌린다.
public static class N6FrameCount
{
    static PropertyInfo Prop() => typeof(EditorWindow).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings")
        .GetProperty("frameCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    static string Set(int value) { var p = Prop(); object before = p.GetValue(null); p.SetValue(null, value); return $"frameCount {before} -> {p.GetValue(null)}"; }
    public static string To200() => Set(200);
    public static string To2000() => Set(2000);
}

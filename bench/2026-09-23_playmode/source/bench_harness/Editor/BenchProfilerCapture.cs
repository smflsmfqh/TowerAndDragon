// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 보존한 `.raw`를 Profiler 창에 올리고 지정한 프레임을 선택해 둔다.
/// 화면 캡처 자체는 에디터 API로 창 하나만 찍을 수 없어 바깥에서 한다 -
/// 여기서는 <b>무엇이 화면에 떠 있는지</b>를 확정하는 일까지만 맡는다.
///
/// 프레임 번호는 raw 기준 `frame_index`다. 프레임 대응이 오프셋 0으로 확인됐으므로
/// 하네스 CSV의 frameIndex와 같은 값을 그대로 쓰면 된다.
/// </summary>
public static class BenchProfilerCapture
{
    private const string PROFILER_WINDOW_TYPE = "UnityEditor.ProfilerWindow";
    private const string RAW_DIRECTORY = "raw";

    public static string Show(string runId, int frameIndex)
    {
        string rawPath = Path.Combine(BenchConfig.AbsoluteOutputRoot, RAW_DIRECTORY, runId + ".raw");

        if (!File.Exists(rawPath))
            return $"raw 없음: {rawPath}";

        if (!ProfilerDriver.LoadProfile(rawPath, false))
            return $"raw 로드 실패: {rawPath}";

        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(PROFILER_WINDOW_TYPE, false))
            .FirstOrDefault(candidate => candidate != null);

        if (windowType == null)
            return "ProfilerWindow 타입을 찾지 못했다.";

        EditorWindow window = EditorWindow.GetWindow(windowType);
        window.Show();
        window.Focus();

        PropertyInfo maximized = windowType.GetProperty("maximized", BindingFlags.Instance | BindingFlags.Public);
        maximized?.SetValue(window, true);

        PropertyInfo selected = windowType.GetProperty("selectedFrameIndex", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo first = windowType.GetProperty("firstAvailableFrameIndex", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo last = windowType.GetProperty("lastAvailableFrameIndex", BindingFlags.Instance | BindingFlags.Public);

        if (selected == null)
            return "selectedFrameIndex를 찾지 못했다.";

        selected.SetValue(window, (long)frameIndex);
        window.Repaint();

        return $"{runId} 로드, frame {frameIndex} 선택 " +
               $"(사용 가능 {first?.GetValue(window)}~{last?.GetValue(window)}), " +
               $"선택 결과 {selected.GetValue(window)}";
    }

    public static string ShowS0Still() => Show("S0_A_run4", 900);
    public static string ShowT1Still() => Show("T1_A_run4", 900);
    public static string ShowT1Transition() => Show("T1_D_run4", 896);
    public static string ShowReentrySpike() => Show("T1_EReenter_run11", 30);

    /// <summary>캡처가 끝나면 최대화를 풀어 원래 레이아웃으로 되돌린다.</summary>
    public static string Restore()
    {
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(PROFILER_WINDOW_TYPE, false))
            .FirstOrDefault(candidate => candidate != null);

        if (windowType == null)
            return "ProfilerWindow 타입을 찾지 못했다.";

        foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(windowType))
        {
            PropertyInfo maximized = windowType.GetProperty("maximized", BindingFlags.Instance | BindingFlags.Public);
            maximized?.SetValue(found, false);
        }

        return "Profiler 창 최대화 해제";
    }
}

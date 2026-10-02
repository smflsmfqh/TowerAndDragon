using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

// N6 전용 임시 스크립트(스크래치, 프로젝트에 넣지 않음). 보존 raw를 Profiler 독립 창에 올리고 프레임을 고른다.
// 보관 헬퍼 BenchProfilerCapture.Show와 같은 일을 하되 창을 최대화하지 않는다(창 하나만 직접 캡처하기 위해).
public static class N6Show
{
    const string RAW_DIR = "bench/2026-09-23_playmode/raw";
    const string FILTER = "TND.Bench";

    public static string S0Still() => Show("S0_A_run4", 900);
    public static string T1Still() => Show("T1_A_run4", 900);
    public static string T1Rebuild() => Show("T1_D_run4", 896);
    public static string ReentrySpike() => Show("T1_EReenter_run11", 30);

    static string Show(string runId, int frame)
    {
        var sb = new StringBuilder();
        string root = Directory.GetParent(Application.dataPath).FullName;
        string path = Path.Combine(root, RAW_DIR, runId + ".raw");
        if (!File.Exists(path)) return "raw 없음: " + path;

        Type us = typeof(EditorWindow).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings");
        sb.AppendLine("frameCount 설정: " + us?.GetProperty("frameCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null));
        if (!ProfilerDriver.LoadProfile(path, false)) return "raw 로드 실패: " + path;
        sb.AppendLine($"로드 {runId}: 사용 가능 {ProfilerDriver.firstFrameIndex}~{ProfilerDriver.lastFrameIndex}");

        ProfilerWindow w = EditorWindow.GetWindow<ProfilerWindow>();
        w.Show();
        if (w.maximized) w.maximized = false;
        w.position = new Rect(80, 80, 1600, 1000);
        SelectCpuModule(w, sb);
        SetHierarchyView(w, sb);
        w.selectedFrameIndex = frame;
        var controller = w.GetFrameTimeViewSampleSelectionController(ProfilerWindow.cpuModuleIdentifier);
        controller.sampleNameSearchFilter = FILTER;
        w.Focus();
        w.Repaint();
        sb.AppendLine($"선택 프레임 {w.selectedFrameIndex}, 모듈 {w.selectedModuleIdentifier}, 검색 '{controller.sampleNameSearchFilter}', 최대화 {w.maximized}, 창 {w.position}");

        using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                   HierarchyFrameDataView.columnTotalTime, false))
        {
            sb.AppendLine($"스레드 {view.threadName}, 프레임 시간 {view.frameTimeMs:F3} ms");
            var stack = new System.Collections.Generic.List<int> { view.GetRootItemID() };
            var children = new System.Collections.Generic.List<int>();
            while (stack.Count > 0)
            {
                int id = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                string name = view.GetItemName(id);
                if (name != null && name.StartsWith(FILTER))
                    sb.AppendLine($"  {name}: total {view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime):F4} ms, " +
                                  $"calls {view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls)}, " +
                                  $"gc {view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory)} B");
                view.GetItemChildren(id, children);
                stack.AddRange(children);
            }
        }
        return sb.ToString();
    }

    static void SelectCpuModule(ProfilerWindow w, StringBuilder sb)
    {
        // 공개 SelectModule이 없어 비공개 메서드·속성을 찾는다. 실패해도 진행하고 결과만 적는다.
        const BindingFlags ALL = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        MethodInfo select = typeof(ProfilerWindow).GetMethods(ALL)
            .FirstOrDefault(m => m.Name == "SelectModule" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
        if (select != null) { select.Invoke(w, new object[] { ProfilerWindow.cpuModuleIdentifier }); return; }
        PropertyInfo prop = typeof(ProfilerWindow).GetProperty("selectedModuleIdentifier", ALL);
        MethodInfo setter = prop?.GetSetMethod(true);
        if (setter != null) { setter.Invoke(w, new object[] { ProfilerWindow.cpuModuleIdentifier }); return; }
        sb.AppendLine("CPU 모듈 선택 방법 없음: " + string.Join(",", typeof(ProfilerWindow).GetMethods(ALL).Where(m => m.Name.Contains("Module")).Select(m => m.Name).Distinct()));
    }

    static void SetHierarchyView(ProfilerWindow w, StringBuilder sb)
    {
        // CPU 모듈의 보기 방식을 Hierarchy로. 공개 API가 없어 내부 속성을 쓴다 - 실패해도 진행하고 결과만 적는다.
        try
        {
            MethodInfo getModule = typeof(ProfilerWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetProfilerModuleByType" && !m.IsGenericMethod);
            Type cpuType = typeof(ProfilerWindow).Assembly.GetType("UnityEditorInternal.Profiling.CPUProfilerModule");
            object module = getModule?.Invoke(w, new object[] { cpuType });
            PropertyInfo viewType = cpuType?.GetProperty("ViewType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (module != null && viewType != null)
            {
                viewType.SetValue(module, Enum.ToObject(viewType.PropertyType, 0));
                sb.AppendLine("보기: " + viewType.GetValue(module));
            }
            else sb.AppendLine($"보기 설정 못함 (module {module != null}, ViewType {viewType != null})");
        }
        catch (Exception e) { sb.AppendLine("보기 설정 예외: " + e.GetType().Name + " " + e.Message); }
    }

    public static string Close()
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<ProfilerWindow>()) w.Close();
        ProfilerDriver.ClearAllFrames();
        return "Profiler 창 닫음, 로드한 프레임 비움";
    }
}

// 점령 하이라이트 실측 하네스 - 측정 전용. 커밋 대상이 아니며 M8에서 삭제한다.

using System;
using System.Linq;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 프로파일러 보유 프레임 수(Frame Count) 설정. 이 값을 쥐고 있는
/// <c>UnityEditor.Profiling.ProfilerUserSettings</c>는 internal이라 직접 참조할 수 없어 리플렉션으로 만진다.
///
/// 이름이 같은 API가 같은 일을 한다고 가정하지 않는다(§3-3) - 프로퍼티를 찾지 못하면 조용히 넘기지 않고
/// false를 돌려주고, 호출한 쪽이 그 사실을 결과에 남긴다. 기본값 300으로 두면 1,800프레임 중
/// 일부만 raw에 남는다(§2-5).
/// </summary>
public static class BenchProfilerSettings
{
    private const string SETTINGS_TYPE_NAME = "UnityEditor.Profiling.ProfilerUserSettings";
    private const string FRAME_COUNT_PROPERTY = "frameCount";

    /// <summary>현재 보유 프레임 수. 읽지 못하면 -1.</summary>
    public static int FrameCount
    {
        get
        {
            PropertyInfo property = ResolveFrameCountProperty();
            return property != null ? (int)property.GetValue(null) : -1;
        }
    }

    /// <summary>보유 프레임 수를 설정한다. 프로퍼티를 찾지 못하면 false.</summary>
    public static bool TrySetFrameCount(int frameCount)
    {
        PropertyInfo property = ResolveFrameCountProperty();

        if (property == null || !property.CanWrite)
        {
            Debug.LogError(
                $"[BENCH] {SETTINGS_TYPE_NAME}.{FRAME_COUNT_PROPERTY}를 찾지 못했다 - " +
                "Profiler 창에서 Frame Count를 직접 올리고 그 사실을 summary에 남긴다.");
            return false;
        }

        property.SetValue(null, frameCount);
        return true;
    }

    /// <summary>Deep Profile·Profile Editor를 끈다(§2-5). 이쪽은 public API라 직접 만진다.</summary>
    public static void DisableDeepProfiling()
    {
        ProfilerDriver.deepProfiling = false;
        ProfilerDriver.profileEditor = false;
    }

    private static PropertyInfo ResolveFrameCountProperty()
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(SETTINGS_TYPE_NAME, false))
            .FirstOrDefault(candidate => candidate != null);

        return type?.GetProperty(FRAME_COUNT_PROPERTY, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    }
}

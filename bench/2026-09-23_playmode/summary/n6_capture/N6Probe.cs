using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class N6Probe
{
    public static string Execute()
    {
        var sb = new StringBuilder();
        Type windowType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEditor.ProfilerWindow", false)).FirstOrDefault(t => t != null);
        sb.AppendLine("ProfilerWindow type: " + (windowType != null));
        foreach (var w in Resources.FindObjectsOfTypeAll(windowType).Cast<EditorWindow>())
        {
            PropertyInfo docked = typeof(EditorWindow).GetProperty("docked", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            sb.AppendLine($"window: title={w.titleContent.text} docked={docked?.GetValue(w)} maximized={w.maximized} pos={w.position}");
        }
        Type settings = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEditor.Profiling.ProfilerUserSettings", false)).FirstOrDefault(t => t != null);
        PropertyInfo frameCount = settings?.GetProperty("frameCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        sb.AppendLine("ProfilerUserSettings type: " + (settings != null) + ", frameCount: " + frameCount?.GetValue(null));
        sb.AppendLine("Unity " + Application.unityVersion);
        return sb.ToString();
    }
}
